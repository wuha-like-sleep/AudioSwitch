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
namespace {
SERVICE_STATUS_HANDLE statusHandle = nullptr;
SERVICE_STATUS serviceStatus{SERVICE_WIN32_OWN_PROCESS, SERVICE_START_PENDING, 0, NO_ERROR, 0, 0, 0};
HANDLE stopEvent = nullptr;
LONG sessionEpoch = 0;
void Status(DWORD state) { serviceStatus.dwCurrentState = state; serviceStatus.dwControlsAccepted = state == SERVICE_RUNNING ? SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN | SERVICE_ACCEPT_SESSIONCHANGE : 0; SetServiceStatus(statusHandle, &serviceStatus); }
DWORD WINAPI Control(DWORD command, DWORD, void*, void*) { if (command == SERVICE_CONTROL_SESSIONCHANGE) { InterlockedIncrement(&sessionEpoch); return NO_ERROR; } if (command == SERVICE_CONTROL_STOP || command == SERVICE_CONTROL_SHUTDOWN) { Status(SERVICE_STOP_PENDING); SetEvent(stopEvent); } return NO_ERROR; }
bool Paired(uint64_t address) {
    BLUETOOTH_FIND_RADIO_PARAMS parameters{sizeof(parameters)}; HANDLE radio = nullptr;
    auto find = BluetoothFindFirstRadio(&parameters, &radio); if (!find) return false;
    bool paired = false;
    do {
        BLUETOOTH_DEVICE_INFO device{}; device.dwSize = sizeof(device); device.Address.ullLong = address;
        paired = BluetoothGetDeviceInfo(radio, &device) == ERROR_SUCCESS && device.fAuthenticated;
        CloseHandle(radio); radio = nullptr;
        if (paired) break;
    } while (BluetoothFindNextRadio(find, &radio));
    BluetoothFindRadioClose(find); return paired;
}
bool VerifyPhone(uint64_t address) {
    if (!address || !Paired(address) || WaitForSingleObject(stopEvent, 0) == WAIT_OBJECT_0) return false;
    auto socket = ::socket(AF_BTH, SOCK_STREAM, BTHPROTO_RFCOMM); if (socket == INVALID_SOCKET) return false;
    ULONG required = TRUE;
    bool valid = setsockopt(socket, SOL_RFCOMM, SO_BTH_AUTHENTICATE, reinterpret_cast<const char*>(&required), sizeof(required)) == 0 && setsockopt(socket, SOL_RFCOMM, SO_BTH_ENCRYPT, reinterpret_cast<const char*>(&required), sizeof(required)) == 0;
    u_long nonblocking = 1;
    valid = valid && ioctlsocket(socket, FIONBIO, &nonblocking) == 0;
    SOCKADDR_BTH endpoint{}; endpoint.addressFamily = AF_BTH; endpoint.btAddr = address;
    endpoint.serviceClassId = {0x0000111f, 0x0000, 0x1000, {0x80, 0, 0, 0x80, 0x5f, 0x9b, 0x34, 0xfb}};
    if (valid) {
        const int connected = connect(socket, reinterpret_cast<SOCKADDR*>(&endpoint), sizeof(endpoint));
        if (connected != 0) {
            const int error = WSAGetLastError(); valid = error == WSAEWOULDBLOCK || error == WSAEINPROGRESS;
            const auto deadline = GetTickCount64() + 8000;
            bool complete = false;
            while (valid && GetTickCount64() < deadline && WaitForSingleObject(stopEvent, 0) != WAIT_OBJECT_0) {
                fd_set writes, errors; FD_ZERO(&writes); FD_ZERO(&errors); FD_SET(socket, &writes); FD_SET(socket, &errors); timeval timeout{0, 100000};
                const int selected = select(0, nullptr, &writes, &errors, &timeout);
                if (selected < 0) { valid = false; break; }
                if (selected > 0) { complete = FD_ISSET(socket, &writes) && !FD_ISSET(socket, &errors); break; }
            }
            valid = valid && complete;
        }
        SOCKADDR_BTH actual{}; int bytes = sizeof(actual);
        valid = valid && getpeername(socket, reinterpret_cast<SOCKADDR*>(&actual), &bytes) == 0 && actual.addressFamily == AF_BTH && actual.btAddr == address;
    }
    closesocket(socket); return valid && WaitForSingleObject(stopEvent, 0) != WAIT_OBJECT_0;
}
bool IsLogonClient(HANDLE pipe, const Bridge::Request& request) {
    ULONG pid = 0; DWORD session = 0;
    if (!GetNamedPipeClientProcessId(pipe, &pid) || !ProcessIdToSessionId(pid, &session) || session != request.session) return false;
    auto process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid); if (!process) return false;
    wchar_t image[32768]{}, expected[MAX_PATH]{}; DWORD length = _countof(image);
    const bool imageRead = QueryFullProcessImageNameW(process, 0, image, &length) != FALSE;
    GetSystemDirectoryW(expected, _countof(expected)); wcscat_s(expected, L"\\LogonUI.exe");
    HANDLE token = nullptr;
    bool valid = imageRead && _wcsicmp(image, expected) == 0 && OpenProcessToken(process, TOKEN_QUERY, &token);
    BYTE storage[512]{}; DWORD bytes = 0;
    valid = valid && GetTokenInformation(token, TokenUser, storage, sizeof(storage), &bytes) && IsWellKnownSid(reinterpret_cast<TOKEN_USER*>(storage)->User.Sid, WinLocalSystemSid);
    if (token) CloseHandle(token); CloseHandle(process);
    if (!valid) return false;
    // A real locked session is required; never use this bridge for boot-time autologon or other users.
    PWTSINFOEXW info = nullptr; DWORD infoBytes = 0;
    valid = WTSQuerySessionInformationW(WTS_CURRENT_SERVER_HANDLE, session, WTSSessionInfoEx, reinterpret_cast<PWSTR*>(&info), &infoBytes) && infoBytes >= sizeof(WTSINFOEXW) && info->Level == 1 && info->Data.WTSInfoExLevel1.SessionFlags == WTS_SESSIONSTATE_LOCK;
    if (info) WTSFreeMemory(info);
    HANDLE userToken = nullptr;
    valid = valid && WTSQueryUserToken(session, &userToken);
    PSID sid = nullptr;
    valid = valid && ConvertStringSidToSidW(request.sid, &sid) && GetTokenInformation(userToken, TokenUser, storage, sizeof(storage), &bytes) && EqualSid(sid, reinterpret_cast<TOKEN_USER*>(storage)->User.Sid);
    if (sid) LocalFree(sid); if (userToken) CloseHandle(userToken); return valid;
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
                const auto epoch = InterlockedCompareExchange(&sessionEpoch, 0, 0);
                if (!IsLogonClient(pipe, request)) response.result = Bridge::Result::SessionMismatch;
                else {
                    uint64_t phone = 0;
                    if (!Bridge::ReadBinding(request.sid, &phone)) response.result = Bridge::Result::NotBound;
                    else if (!phone || !VerifyPhone(phone)) response.result = Bridge::Result::PhoneUnavailable;
                    else {
                        uint64_t current = 0;
                        if (epoch == InterlockedCompareExchange(&sessionEpoch, 0, 0) && IsLogonClient(pipe, request) && Bridge::ReadBinding(request.sid, &current) && current == phone) { response.result = Bridge::Result::Verified; response.phone = phone; }
                        else response.result = Bridge::Result::SessionMismatch;
                    }
                }
                response.completed = GetTickCount64(); Bridge::Transfer(pipe, &response, sizeof(response), true, stopEvent);
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
    if (wcscmp(argv[1], L"--unbind") == 0) { const auto error = RegDeleteKeyExW(HKEY_LOCAL_MACHINE, path, KEY_WOW64_64KEY, 0); return error == ERROR_FILE_NOT_FOUND ? ERROR_SUCCESS : static_cast<int>(error); }
    if (argc != 4 || wcslen(argv[3]) != 12) return ERROR_INVALID_PARAMETER;
    uint64_t address = 0;
    for (size_t index = 0; index < 12; ++index) { const auto c = argv[3][index]; unsigned digit; if (c >= L'0' && c <= L'9') digit = c - L'0'; else if (c >= L'A' && c <= L'F') digit = c - L'A' + 10; else if (c >= L'a' && c <= L'f') digit = c - L'a' + 10; else return ERROR_INVALID_PARAMETER; address = (address << 4) | digit; }
    if (!address || !Paired(address)) return ERROR_NOT_FOUND;
    HKEY key = nullptr; auto error = RegCreateKeyExW(HKEY_LOCAL_MACHINE, path, 0, nullptr, 0, KEY_SET_VALUE | KEY_WOW64_64KEY, nullptr, &key, nullptr);
    if (error == ERROR_SUCCESS) { error = RegSetValueExW(key, L"Phone", 0, REG_SZ, reinterpret_cast<const BYTE*>(argv[3]), 13 * sizeof(wchar_t)); RegCloseKey(key); }
    return static_cast<int>(error);
}
}
int wmain(int argc, wchar_t** argv) {
    if (argc == 2 && (wcscmp(argv[1], L"--install-service") == 0 || wcscmp(argv[1], L"--stop-service") == 0 || wcscmp(argv[1], L"--remove-service") == 0)) return ManageService(argv[1]);
    if (argc > 1) return Binding(argc, argv);
    SERVICE_TABLE_ENTRYW entries[] = {{const_cast<PWSTR>(Bridge::ServiceName), ServiceMain}, {nullptr, nullptr}};
    return StartServiceCtrlDispatcherW(entries) ? 0 : static_cast<int>(GetLastError());
}
