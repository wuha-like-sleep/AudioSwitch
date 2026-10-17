// Copyright (c) 2026 wuha-like-sleep. Licensed under the MIT license.
#include <winsock2.h>
#include <ws2bth.h>
#include <windows.h>
#include <bluetoothapis.h>
#include <sddl.h>
#include <wtsapi32.h>
#include <shlobj.h>
#include <cstdio>
#include "BridgeProtocol.h"
#include "LoginDiagnostics.h"
#include "BridgeClient.h"
#include "CredentialVault.h"
#include "AutoPolicy.h"
#include <array>
namespace {
SERVICE_STATUS_HANDLE statusHandle = nullptr;
SERVICE_STATUS serviceStatus{SERVICE_WIN32_OWN_PROCESS, SERVICE_START_PENDING, 0, NO_ERROR, 0, 0, 0};
HANDLE stopEvent = nullptr;
LONG sessionEpoch = 0;
SRWLOCK policyLock = SRWLOCK_INIT;
std::array<Bridge::AutoPolicy, 64> policies{};
Bridge::AutoPolicy* Policy(DWORD session) {
    for (auto& value : policies) if (value.session == session) return &value;
    for (auto& value : policies) if (!value.session) { value.session = session; return &value; }
    return nullptr;
}
void Status(DWORD state) { serviceStatus.dwCurrentState = state; serviceStatus.dwControlsAccepted = state == SERVICE_RUNNING ? SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN | SERVICE_ACCEPT_SESSIONCHANGE : 0; SetServiceStatus(statusHandle, &serviceStatus); }
DWORD WINAPI Control(DWORD command, DWORD event, void* data, void*) {
    if (command == SERVICE_CONTROL_SESSIONCHANGE) {
        InterlockedIncrement(&sessionEpoch);
        if (data && (event == WTS_SESSION_LOCK || event == WTS_SESSION_UNLOCK || event == WTS_SESSION_LOGON || event == WTS_SESSION_LOGOFF)) {
            const auto session = static_cast<WTSSESSION_NOTIFICATION*>(data)->dwSessionId;
            AcquireSRWLockExclusive(&policyLock); auto state = Policy(session); if (state) { *state = {}; state->session = session; } ReleaseSRWLockExclusive(&policyLock);
        }
        return NO_ERROR;
    }
    if (command == SERVICE_CONTROL_STOP || command == SERVICE_CONTROL_SHUTDOWN) { Status(SERVICE_STOP_PENDING); SetEvent(stopEvent); }
    return NO_ERROR;
}
bool Paired(uint64_t address, DWORD* error = nullptr, Bridge::PhoneStage* stage = nullptr) {
    if (stage) *stage = Bridge::PhoneStage::Adapter;
    BLUETOOTH_FIND_RADIO_PARAMS parameters{sizeof(parameters)}; HANDLE radio = nullptr;
    auto find = BluetoothFindFirstRadio(&parameters, &radio);
    if (!find) { if (error) { *error = GetLastError(); if (!*error) *error = ERROR_NOT_READY; } return false; }
    if (stage) *stage = Bridge::PhoneStage::Pairing;
    bool paired = false;
    do {
        BLUETOOTH_DEVICE_INFO device{}; device.dwSize = sizeof(device); device.Address.ullLong = address;
        paired = BluetoothGetDeviceInfo(radio, &device) == ERROR_SUCCESS && device.fAuthenticated;
        CloseHandle(radio); radio = nullptr;
        if (paired) break;
    } while (BluetoothFindNextRadio(find, &radio));
    BluetoothFindRadioClose(find);
    if (error) *error = paired ? ERROR_SUCCESS : ERROR_NOT_FOUND;
    return paired;
}
struct PhoneCheck { bool known = false, verified = false; DWORD error = 0; Bridge::PhoneStage stage = Bridge::PhoneStage::None; };
PhoneCheck VerifyPhone(uint64_t address) {
    PhoneCheck check;
    check.known = Paired(address, &check.error, &check.stage);
    if (!check.known) return check;
    if (WaitForSingleObject(stopEvent, 0) == WAIT_OBJECT_0) { check.error = ERROR_CANCELLED; check.stage = Bridge::PhoneStage::Cancelled; return check; }
    check.stage = Bridge::PhoneStage::Socket;
    auto socket = ::socket(AF_BTH, SOCK_STREAM, BTHPROTO_RFCOMM);
    if (socket == INVALID_SOCKET) { check.error = WSAGetLastError(); return check; }
    WSAEVENT event = WSA_INVALID_EVENT;
    auto connectPhone = [&]() -> bool {
        ULONG required = TRUE;
        check.stage = Bridge::PhoneStage::Authentication;
        if (setsockopt(socket, SOL_RFCOMM, SO_BTH_AUTHENTICATE, reinterpret_cast<const char*>(&required), sizeof(required)) != 0) { check.error = WSAGetLastError(); return false; }
        check.stage = Bridge::PhoneStage::Encryption;
        if (setsockopt(socket, SOL_RFCOMM, SO_BTH_ENCRYPT, reinterpret_cast<const char*>(&required), sizeof(required)) != 0) { check.error = WSAGetLastError(); return false; }
        check.stage = Bridge::PhoneStage::Events;
        event = WSACreateEvent();
        if (event == WSA_INVALID_EVENT || WSAEventSelect(socket, event, FD_CONNECT) != 0) { check.error = WSAGetLastError(); return false; }
        SOCKADDR_BTH endpoint{}; endpoint.addressFamily = AF_BTH; endpoint.btAddr = address;
        endpoint.serviceClassId = {0x0000111f, 0x0000, 0x1000, {0x80, 0, 0, 0x80, 0x5f, 0x9b, 0x34, 0xfb}};
        check.stage = Bridge::PhoneStage::Connect;
        if (connect(socket, reinterpret_cast<SOCKADDR*>(&endpoint), sizeof(endpoint)) != 0) {
            check.error = WSAGetLastError();
            if (check.error != WSAEWOULDBLOCK && check.error != WSAEINPROGRESS) return false;
            check.stage = Bridge::PhoneStage::Wait;
            HANDLE waits[] = {event, stopEvent};
            const auto waited = WaitForMultipleObjects(2, waits, FALSE, 8000);
            if (waited == WAIT_OBJECT_0 + 1) { check.error = ERROR_CANCELLED; check.stage = Bridge::PhoneStage::Cancelled; return false; }
            if (waited == WAIT_TIMEOUT) { check.error = WSAETIMEDOUT; return false; }
            if (waited != WAIT_OBJECT_0) { check.error = GetLastError(); return false; }
            WSANETWORKEVENTS events{};
            if (WSAEnumNetworkEvents(socket, event, &events) != 0) { check.error = WSAGetLastError(); return false; }
            if (!(events.lNetworkEvents & FD_CONNECT)) { check.error = WSAEINVAL; return false; }
            check.error = static_cast<DWORD>(events.iErrorCode[FD_CONNECT_BIT]);
            if (check.error) return false;
        }
        check.stage = Bridge::PhoneStage::Peer;
        SOCKADDR_BTH actual{}; int bytes = sizeof(actual);
        if (getpeername(socket, reinterpret_cast<SOCKADDR*>(&actual), &bytes) != 0) { check.error = WSAGetLastError(); return false; }
        if (actual.addressFamily != AF_BTH || actual.btAddr != address) { check.error = ERROR_INVALID_DATA; return false; }
        if (WaitForSingleObject(stopEvent, 0) == WAIT_OBJECT_0) { check.error = ERROR_CANCELLED; check.stage = Bridge::PhoneStage::Cancelled; return false; }
        check.error = 0; check.stage = Bridge::PhoneStage::None; return true;
    };
    check.verified = connectPhone();
    closesocket(socket); if (event != WSA_INVALID_EVENT) WSACloseEvent(event);
    return check;
}
bool IsLogonClient(HANDLE pipe, const Bridge::Request& request) {
    const bool enrollment = request.intent == Bridge::Intent::Enroll || request.intent == Bridge::Intent::ServiceProbe;
    ULONG pid = 0; DWORD session = 0;
    if (!GetNamedPipeClientProcessId(pipe, &pid) || !ProcessIdToSessionId(pid, &session) || session != request.session) return false;
    auto process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid); if (!process) return false;
    wchar_t image[32768]{}, expected[MAX_PATH]{}; DWORD length = _countof(image);
    const bool imageRead = QueryFullProcessImageNameW(process, 0, image, &length) != FALSE;
    if (enrollment) {
        PWSTR folder = nullptr;
        if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_ProgramFiles, 0, nullptr, &folder))) { swprintf_s(expected, L"%s\\AudioSwitch Login Lab\\AudioSwitch.LoginBridge.exe", folder); CoTaskMemFree(folder); }
    } else { GetSystemDirectoryW(expected, _countof(expected)); wcscat_s(expected, L"\\LogonUI.exe"); }
    HANDLE token = nullptr;
    bool valid = imageRead && _wcsicmp(image, expected) == 0 && OpenProcessToken(process, TOKEN_QUERY, &token);
    alignas(TOKEN_USER) BYTE storage[512]{}; DWORD bytes = 0;
    if (enrollment) {
        TOKEN_ELEVATION elevation{};
        valid = valid && GetTokenInformation(token, TokenElevation, &elevation, sizeof(elevation), &bytes) && elevation.TokenIsElevated;
    } else valid = valid && GetTokenInformation(token, TokenUser, storage, sizeof(storage), &bytes) && IsWellKnownSid(reinterpret_cast<TOKEN_USER*>(storage)->User.Sid, WinLocalSystemSid);
    if (token) CloseHandle(token); CloseHandle(process);
    if (!valid) return false;
    // A real locked session is required; never use this bridge for boot-time autologon or other users.
    PWTSINFOEXW info = nullptr; DWORD infoBytes = 0;
    valid = enrollment || (WTSQuerySessionInformationW(WTS_CURRENT_SERVER_HANDLE, session, WTSSessionInfoEx, reinterpret_cast<PWSTR*>(&info), &infoBytes) && infoBytes >= sizeof(WTSINFOEXW) && info->Level == 1 && info->Data.WTSInfoExLevel1.SessionFlags == WTS_SESSIONSTATE_LOCK);
    if (info) WTSFreeMemory(info);
    HANDLE userToken = nullptr;
    valid = valid && WTSQueryUserToken(session, &userToken);
    PSID sid = nullptr;
    valid = valid && ConvertStringSidToSidW(request.sid, &sid) && GetTokenInformation(userToken, TokenUser, storage, sizeof(storage), &bytes) && EqualSid(sid, reinterpret_cast<TOKEN_USER*>(storage)->User.Sid);
    if (sid) LocalFree(sid); if (userToken) CloseHandle(userToken); return valid;
}
DWORD SaveEnrollment(const Bridge::Request& request, const BYTE* encrypted) {
    Vault::Record record;
    if (!Vault::Decrypt(encrypted, request.payloadBytes, record) || wcscmp(record.sid, request.sid) != 0) return ERROR_INVALID_DATA;
    uint64_t phone = 0; if (!Bridge::ReadBinding(record.sid, &phone) || phone != record.phone) return ERROR_NOT_FOUND;
    HANDLE user = nullptr;
    if (!LogonUserW(record.qualified + 17, L"MicrosoftAccount", record.password, LOGON32_LOGON_INTERACTIVE, LOGON32_PROVIDER_WINNT50, &user)) return GetLastError();
    alignas(TOKEN_USER) BYTE tokenData[512]{}; DWORD size = 0; PSID expected = nullptr;
    const bool matched = ConvertStringSidToSidW(record.sid, &expected) && GetTokenInformation(user, TokenUser, tokenData, sizeof(tokenData), &size) && EqualSid(expected, reinterpret_cast<TOKEN_USER*>(tokenData)->User.Sid);
    if (expected) LocalFree(expected); CloseHandle(user);
    if (!matched) return ERROR_NO_SUCH_USER;
    return Vault::Save(record) ? ERROR_SUCCESS : ERROR_WRITE_FAULT;
}
void VerifyRequest(HANDLE pipe, const Bridge::Request& request, Bridge::Response& response) {
    const auto epoch = InterlockedCompareExchange(&sessionEpoch, 0, 0);
    if (!IsLogonClient(pipe, request)) { response.result = Bridge::Result::SessionMismatch; return; }
    DWORD phoneStage = 0;
    struct Diagnostic { const Bridge::Request& request; const Bridge::Response& response; const DWORD& phoneStage;
        ~Diagnostic() { LoginDiagnostics::Write(request.sid, request.intent == Bridge::Intent::Enroll ? L"Enrollment" : request.intent == Bridge::Intent::AutoCommit ? L"Commit" : request.intent == Bridge::Intent::ServiceProbe ? L"ServiceProbe" : L"Phone", static_cast<DWORD>(response.result), response.error, phoneStage); }
    } diagnostic{request, response, phoneStage};
    if (request.intent == Bridge::Intent::Enroll) {
        BYTE encrypted[16384]{};
        if (!Bridge::Transfer(pipe, encrypted, request.payloadBytes, false, stopEvent)) { response.error = ERROR_READ_FAULT; return; }
        response.error = IsLogonClient(pipe, request) ? SaveEnrollment(request, encrypted) : ERROR_ACCESS_DENIED;
        SecureZeroMemory(encrypted, sizeof(encrypted));
        response.result = response.error ? Bridge::Result::Denied : Bridge::Result::Enrolled; return;
    }
    uint64_t phone = 0;
    if (!Bridge::ReadBinding(request.sid, &phone) || !phone) { response.result = Bridge::Result::NotBound; return; }
    const bool automatic = request.intent == Bridge::Intent::AutoProbe || request.intent == Bridge::Intent::AutoCommit;
    if (automatic && !Vault::Enabled(request.sid)) return;
    if (request.intent == Bridge::Intent::AutoCommit) {
        AcquireSRWLockExclusive(&policyLock); auto state = Policy(request.session);
        const bool allowed = state && epoch == InterlockedCompareExchange(&sessionEpoch, 0, 0) && state->Commit(request.proof, GetTickCount64(), phone, epoch);
        if (allowed) response.completed = state->completed;
        ReleaseSRWLockExclusive(&policyLock);
        if (allowed) { response.result = Bridge::Result::Verified; response.phone = phone; } return;
    }
    if (automatic) {
        AcquireSRWLockShared(&policyLock); bool attempted = false; for (auto& state : policies) if (state.session == request.session) attempted = state.attempted; ReleaseSRWLockShared(&policyLock);
        if (attempted) { response.result = Bridge::Result::AlreadyTried; return; }
    }
    // No usable adapter / paired identity cannot arm an away-to-near transition.
    const auto phoneCheck = VerifyPhone(phone);
    response.error = phoneCheck.error; phoneStage = static_cast<DWORD>(phoneCheck.stage);
    const bool present = phoneCheck.verified;
    uint64_t current = 0;
    if (epoch != InterlockedCompareExchange(&sessionEpoch, 0, 0) || !IsLogonClient(pipe, request) || !Bridge::ReadBinding(request.sid, &current) || current != phone) { response.result = Bridge::Result::SessionMismatch; return; }
    if (!present) {
        if (automatic && phoneCheck.known && Bridge::CanArmAbsence(phoneCheck.error, phoneCheck.stage)) { AcquireSRWLockExclusive(&policyLock); auto state = Policy(request.session); if (state) state->Absent(GetTickCount64()); ReleaseSRWLockExclusive(&policyLock); }
        response.result = Bridge::Result::PhoneUnavailable; return;
    }
    if (automatic) {
        AcquireSRWLockExclusive(&policyLock); auto state = Policy(request.session);
        const bool ready = state && state->Present(request.nonce, GetTickCount64(), phone, epoch);
        ReleaseSRWLockExclusive(&policyLock);
        if (!ready) { response.result = Bridge::Result::Waiting; return; }
    }
    response.result = Bridge::Result::Verified; response.phone = phone;
}
void Serve() {
    PSECURITY_DESCRIPTOR descriptor = nullptr;
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(L"D:P(A;;GA;;;SY)(A;;GRGW;;;BA)", SDDL_REVISION_1, &descriptor, nullptr)) return;
    SECURITY_ATTRIBUTES security{sizeof(security), descriptor, FALSE};
    while (WaitForSingleObject(stopEvent, 0) != WAIT_OBJECT_0) {
        auto pipe = CreateNamedPipeW(Bridge::PipeName, PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE, PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS, 1, sizeof(Bridge::Response), sizeof(Bridge::Request), 2000, &security);
        if (pipe == INVALID_HANDLE_VALUE) break; // A pre-existing server is never trusted or reused.
        OVERLAPPED operation{}; operation.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!operation.hEvent) { CloseHandle(pipe); break; }
        BOOL connected = ConnectNamedPipe(pipe, &operation);
        const auto error = GetLastError();
        if (!connected && error == ERROR_PIPE_CONNECTED) connected = TRUE;
        else if (!connected && error == ERROR_IO_PENDING) {
            HANDLE waits[] = {operation.hEvent, stopEvent};
            if (WaitForMultipleObjects(2, waits, FALSE, INFINITE) == WAIT_OBJECT_0) { DWORD count = 0; connected = GetOverlappedResult(pipe, &operation, &count, FALSE); }
            else { CancelIoEx(pipe, &operation); DWORD count = 0; GetOverlappedResult(pipe, &operation, &count, TRUE); }
        }
        CloseHandle(operation.hEvent);
        if (connected) {
            Bridge::Request request; Bridge::Response response;
            if (Bridge::Transfer(pipe, &request, sizeof(request), false, stopEvent) && Bridge::ValidRequest(request)) {
                response.session = request.session; memcpy(response.nonce, request.nonce, sizeof(response.nonce));
                VerifyRequest(pipe, request, response);
                if (!response.completed) response.completed = GetTickCount64();
                if (Bridge::Transfer(pipe, &response, sizeof(response), true, stopEvent)) { BYTE acknowledged[32]{}; Bridge::Transfer(pipe, acknowledged, sizeof(acknowledged), false, stopEvent); }
            }
        }
        DisconnectNamedPipe(pipe); CloseHandle(pipe);
    }
    LocalFree(descriptor);
}
void WINAPI ServiceMain(DWORD, PWSTR*) {
    statusHandle = RegisterServiceCtrlHandlerExW(Bridge::ServiceName, Control, nullptr); if (!statusHandle) return;
    stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    WSADATA winsock{};
    if (!stopEvent || WSAStartup(MAKEWORD(2, 2), &winsock) != 0) { serviceStatus.dwWin32ExitCode = ERROR_SERVICE_NOT_ACTIVE; Status(SERVICE_STOPPED); if (stopEvent) CloseHandle(stopEvent); return; }
    HANDLE privilegeToken = nullptr; TOKEN_PRIVILEGES privileges{}; privileges.PrivilegeCount = 1;
    if (OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &privilegeToken)) {
        if (LookupPrivilegeValueW(nullptr, SE_TCB_NAME, &privileges.Privileges[0].Luid)) { privileges.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED; AdjustTokenPrivileges(privilegeToken, FALSE, &privileges, 0, nullptr, nullptr); }
        CloseHandle(privilegeToken);
    }
    Status(SERVICE_RUNNING); Serve(); Status(SERVICE_STOP_PENDING); WSACleanup(); CloseHandle(stopEvent); Status(SERVICE_STOPPED);
}
int ManageService(PCWSTR operation) {
    if (!IsUserAnAdmin()) return ERROR_ELEVATION_REQUIRED;
    if (wcscmp(operation, L"--install-service") == 0) { const auto stopped = ManageService(L"--stop-service"); if (stopped != ERROR_SUCCESS) return stopped; }
    auto manager = OpenSCManagerW(nullptr, nullptr, SC_MANAGER_CONNECT | SC_MANAGER_CREATE_SERVICE); if (!manager) return static_cast<int>(GetLastError());
    auto service = OpenServiceW(manager, Bridge::ServiceName, SERVICE_QUERY_STATUS | SERVICE_START | SERVICE_STOP | SERVICE_CHANGE_CONFIG | DELETE);
    DWORD error = ERROR_SUCCESS;
    if (wcscmp(operation, L"--install-service") == 0) {
        wchar_t executable[32768]{}; if (!GetModuleFileNameW(nullptr, executable, _countof(executable))) error = GetLastError();
        PWSTR programFiles = nullptr;
        if (FAILED(SHGetKnownFolderPath(FOLDERID_ProgramFiles, KF_FLAG_DEFAULT, nullptr, &programFiles))) error = ERROR_ACCESS_DENIED;
        wchar_t expected[32768]{};
        if (programFiles) { swprintf_s(expected, L"%s\\AudioSwitch Login Lab\\AudioSwitch.LoginBridge.exe", programFiles); CoTaskMemFree(programFiles); }
        if (!*expected || _wcsicmp(executable, expected) != 0) error = ERROR_ACCESS_DENIED;
        wchar_t command[32772]{}; swprintf_s(command, L"\"%s\"", executable);
        if (!error && !service) service = CreateServiceW(manager, Bridge::ServiceName, L"AudioSwitch 登录桥接", SERVICE_QUERY_STATUS | SERVICE_START | SERVICE_STOP | SERVICE_CHANGE_CONFIG | DELETE, SERVICE_WIN32_OWN_PROCESS, SERVICE_AUTO_START, SERVICE_ERROR_NORMAL, command, nullptr, nullptr, nullptr, L"LocalSystem", nullptr);
        else if (!error && !ChangeServiceConfigW(service, SERVICE_WIN32_OWN_PROCESS, SERVICE_AUTO_START, SERVICE_ERROR_NORMAL, command, nullptr, nullptr, nullptr, L"LocalSystem", nullptr, L"AudioSwitch 登录桥接")) error = GetLastError();
        if (!service && !error) error = GetLastError();
        if (service && !error && !StartServiceW(service, 0, nullptr)) { error = GetLastError(); if (error == ERROR_SERVICE_ALREADY_RUNNING) error = ERROR_SUCCESS; }
        if (service && !error) {
            SERVICE_STATUS_PROCESS status{}; DWORD bytes = 0; const auto deadline = GetTickCount64() + 10000;
            while (!error) {
                if (!QueryServiceStatusEx(service, SC_STATUS_PROCESS_INFO, reinterpret_cast<BYTE*>(&status), sizeof(status), &bytes)) { error = GetLastError(); break; }
                if (status.dwCurrentState == SERVICE_RUNNING) break;
                if (status.dwCurrentState == SERVICE_STOPPED || GetTickCount64() >= deadline) { error = ERROR_SERVICE_NOT_ACTIVE; break; }
                Sleep(50);
            }
        }
    } else if (service) {
        SERVICE_STATUS_PROCESS status{}; DWORD bytes = 0;
        if (!QueryServiceStatusEx(service, SC_STATUS_PROCESS_INFO, reinterpret_cast<BYTE*>(&status), sizeof(status), &bytes)) error = GetLastError();
        if (!error && status.dwCurrentState != SERVICE_STOPPED) {
            SERVICE_STATUS ignored{}; if (!ControlService(service, SERVICE_CONTROL_STOP, &ignored)) { error = GetLastError(); if (error == ERROR_SERVICE_NOT_ACTIVE) error = ERROR_SUCCESS; }
            const auto deadline = GetTickCount64() + 10000;
            while (!error) {
                if (!QueryServiceStatusEx(service, SC_STATUS_PROCESS_INFO, reinterpret_cast<BYTE*>(&status), sizeof(status), &bytes)) { error = GetLastError(); break; }
                if (status.dwCurrentState == SERVICE_STOPPED) break;
                if (GetTickCount64() >= deadline) { error = ERROR_TIMEOUT; break; } Sleep(50);
            }
        }
        if (!error && wcscmp(operation, L"--remove-service") == 0 && !DeleteService(service)) error = GetLastError();
    } else { error = GetLastError(); if (error == ERROR_SERVICE_DOES_NOT_EXIST) error = ERROR_SUCCESS; }
    if (service) CloseServiceHandle(service); CloseServiceHandle(manager); return static_cast<int>(error);
}
int Binding(int argc, wchar_t** argv) {
    if (!IsUserAnAdmin()) return ERROR_ELEVATION_REQUIRED;
    if (argc < 3 || (wcscmp(argv[1], L"--bind") != 0 && wcscmp(argv[1], L"--unbind") != 0)) return ERROR_INVALID_PARAMETER;
    PSID sid = nullptr;
    if (wcslen(argv[2]) > 183 || wcspbrk(argv[2], L"\\/") || !ConvertStringSidToSidW(argv[2], &sid)) return ERROR_INVALID_SID;
    LocalFree(sid);
    wchar_t path[256]{}; swprintf_s(path, L"%s%s", Bridge::BindingRoot, argv[2]);
    if (wcscmp(argv[1], L"--unbind") == 0) { Vault::Revoke(argv[2]); const auto error = RegDeleteKeyExW(HKEY_LOCAL_MACHINE, path, KEY_WOW64_64KEY, 0); return error == ERROR_FILE_NOT_FOUND ? ERROR_SUCCESS : static_cast<int>(error); }
    if (argc != 4 || wcslen(argv[3]) != 12) return ERROR_INVALID_PARAMETER;
    uint64_t address = 0;
    for (size_t index = 0; index < 12; ++index) { const auto c = argv[3][index]; unsigned digit; if (c >= L'0' && c <= L'9') digit = c - L'0'; else if (c >= L'A' && c <= L'F') digit = c - L'A' + 10; else if (c >= L'a' && c <= L'f') digit = c - L'a' + 10; else return ERROR_INVALID_PARAMETER; address = (address << 4) | digit; }
    if (!address || !Paired(address)) return ERROR_NOT_FOUND;
    Vault::Revoke(argv[2]);
    HKEY key = nullptr; auto error = RegCreateKeyExW(HKEY_LOCAL_MACHINE, path, 0, nullptr, 0, KEY_SET_VALUE | KEY_WOW64_64KEY, nullptr, &key, nullptr);
    if (error == ERROR_SUCCESS) { error = RegSetValueExW(key, L"Phone", 0, REG_SZ, reinterpret_cast<const BYTE*>(argv[3]), 13 * sizeof(wchar_t)); RegCloseKey(key); }
    return static_cast<int>(error);
}
}
int wmain(int argc, wchar_t** argv) {
    if (argc == 2 && (wcscmp(argv[1], L"--install-service") == 0 || wcscmp(argv[1], L"--stop-service") == 0 || wcscmp(argv[1], L"--remove-service") == 0)) return ManageService(argv[1]);
    if (argc == 3 && IsUserAnAdmin() && (wcscmp(argv[1], L"--enroll") == 0 || wcscmp(argv[1], L"--revoke") == 0 || wcscmp(argv[1], L"--probe-service") == 0)) {
        PSID sid = nullptr; if (wcslen(argv[2]) > 183 || !ConvertStringSidToSidW(argv[2], &sid)) return ERROR_INVALID_SID; LocalFree(sid);
        if (wcscmp(argv[1], L"--revoke") == 0) { Vault::Revoke(argv[2]); return 0; }
        if (wcscmp(argv[1], L"--probe-service") == 0) return static_cast<int>(ProbeServicePhone(argv[2]));
        return static_cast<int>(EnrollLocalCredential(argv[2]));
    }
    if (argc > 1) return Binding(argc, argv);
    SERVICE_TABLE_ENTRYW entries[] = {{const_cast<PWSTR>(Bridge::ServiceName), ServiceMain}, {nullptr, nullptr}};
    return StartServiceCtrlDispatcherW(entries) ? 0 : static_cast<int>(GetLastError());
}
