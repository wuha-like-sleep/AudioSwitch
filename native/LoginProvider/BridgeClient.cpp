#include <windows.h>
#include <bcrypt.h>
#include <objbase.h>
#include "BridgeProtocol.h"
#include "LoginDiagnostics.h"
#include "BridgeClient.h"
namespace {
bool IsServiceProcess(HANDLE pipe) {
    ULONG server = 0; if (!GetNamedPipeServerProcessId(pipe, &server)) return false;
    auto manager = OpenSCManagerW(nullptr, nullptr, SC_MANAGER_CONNECT); if (!manager) return false;
    auto service = OpenServiceW(manager, Bridge::ServiceName, SERVICE_QUERY_STATUS);
    SERVICE_STATUS_PROCESS status{}; DWORD size = 0;
    const bool valid = service && QueryServiceStatusEx(service, SC_STATUS_PROCESS_INFO, reinterpret_cast<BYTE*>(&status), sizeof(status), &size) && status.dwCurrentState == SERVICE_RUNNING && status.dwProcessId == server && server != 0;
    if (service) CloseServiceHandle(service); CloseServiceHandle(manager); return valid;
}
}
namespace {
bool Exchange(Bridge::Request& request, Bridge::Response& response, HANDLE cancel, const BYTE* payload = nullptr) {
    DWORD session = 0;
    if (!ProcessIdToSessionId(GetCurrentProcessId(), &session) || BCryptGenRandom(nullptr, request.nonce, sizeof(request.nonce), BCRYPT_USE_SYSTEM_PREFERRED_RNG) < 0) return false;
    request.session = session;
    auto pipe = CreateFileW(Bridge::PipeName, GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED | SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION, nullptr);
    if (pipe == INVALID_HANDLE_VALUE) return false;
    DWORD mode = PIPE_READMODE_MESSAGE;
    bool received = SetNamedPipeHandleState(pipe, &mode, nullptr, nullptr) && IsServiceProcess(pipe) && Bridge::Transfer(pipe, &request, sizeof(request), true, cancel);
    if (received && request.payloadBytes) received = payload && Bridge::Transfer(pipe, const_cast<BYTE*>(payload), request.payloadBytes, true, cancel);
    received = received && Bridge::Transfer(pipe, &response, sizeof(response), false, cancel, 12000) && IsServiceProcess(pipe);
    // Acknowledge delivery before the server disconnects its message pipe.
    if (received) received = Bridge::Transfer(pipe, request.nonce, sizeof(request.nonce), true, cancel);
    CloseHandle(pipe);
    return received && response.magic == Bridge::Magic && response.version == Bridge::Version && response.size == sizeof(response) && response.session == request.session && !memcmp(request.nonce, response.nonce, sizeof(request.nonce));
}
bool PhoneRequest(PCWSTR sid, Bridge::Intent intent, PhoneApproval* approval, HANDLE cancel) {
    uint64_t phone = 0;
    if (!Bridge::ReadBinding(sid, &phone)) return intent == Bridge::Intent::Manual;
    if (!phone) return false;
    Bridge::Request request; request.intent = intent;
    if (wcsncpy_s(request.sid, sid, _TRUNCATE) != 0) return false;
    if (intent == Bridge::Intent::AutoCommit && approval) memcpy(request.proof, approval->nonce, sizeof(request.proof));
    Bridge::Response response;
    const bool exchanged = Exchange(request, response, cancel);
    if (!exchanged && (!cancel || WaitForSingleObject(cancel, 0) != WAIT_OBJECT_0))
        LoginDiagnostics::Write(sid, intent == Bridge::Intent::AutoCommit ? L"Commit" : L"Phone", 0, ERROR_SERVICE_NOT_ACTIVE);
    const bool valid = exchanged && Bridge::ValidResponse(request, response, phone, GetTickCount64());
    uint64_t current = 0;
    if (!valid || !Bridge::ReadBinding(sid, &current) || current != phone) return false;
    if (intent == Bridge::Intent::AutoProbe && approval) { memcpy(approval->nonce, request.nonce, sizeof(approval->nonce)); approval->completed = response.completed; }
    return true;
}
}
bool VerifyBoundPhone(PCWSTR sid, HANDLE cancel) { return PhoneRequest(sid, Bridge::Intent::Manual, nullptr, cancel); }
bool ProbeAutomatic(PCWSTR sid, PhoneApproval& approval, HANDLE cancel) { return PhoneRequest(sid, Bridge::Intent::AutoProbe, &approval, cancel); }
bool CommitAutomatic(PCWSTR sid, const PhoneApproval& approval) { auto copy = approval; return PhoneRequest(sid, Bridge::Intent::AutoCommit, &copy, nullptr); }

#include <wincred.h>
#include "CredentialVault.h"
DWORD EnrollLocalCredential(PCWSTR sid) {
    uint64_t phone = 0; if (!Bridge::ReadBinding(sid, &phone) || !phone) return ERROR_NOT_FOUND;
    CREDUI_INFOW info{}; info.cbSize = sizeof(info); info.pszCaptionText = L"AudioSwitch · 本机凭据初始化";
    info.pszMessageText = L"输入微软账户邮箱和账户密码（不是 PIN）。仅在本机验证、加密保存，用于手机验证通过后的自动提交。";
    ULONG package = 0, bytes = 0; void* buffer = nullptr;
    DWORD error = CredUIPromptForWindowsCredentialsW(&info, 0, &package, nullptr, 0, &buffer, &bytes, nullptr, CREDUIWIN_GENERIC);
    if (error != ERROR_SUCCESS) return error;
    Vault::Record record; record.phone = phone; wcsncpy_s(record.sid, sid, _TRUNCATE);
    wchar_t user[512]{}, domain[512]{}; DWORD userSize = 512, domainSize = 512, passwordSize = 1025;
    const bool unpacked = CredUnPackAuthenticationBufferW(0, buffer, bytes, user, &userSize, domain, &domainSize, record.password, &passwordSize) != FALSE;
    error = unpacked ? ERROR_SUCCESS : GetLastError();
    SecureZeroMemory(buffer, bytes); CoTaskMemFree(buffer);
    if (error) return error;
    if (_wcsnicmp(user, L"MicrosoftAccount\\", 17) == 0) wcsncpy_s(record.qualified, user, _TRUNCATE);
    else if ((!*domain || _wcsicmp(domain, L"MicrosoftAccount") == 0) && wcslen(user) < 495) swprintf_s(record.qualified, L"MicrosoftAccount\\%s", user);
    if (!Vault::Valid(record) || !wcschr(record.qualified, L'@')) return ERROR_INVALID_ACCOUNT_NAME;
    if (MessageBoxW(nullptr, L"将验证并在本机加密保存账户密码，启用实验性自动提交。管理员和系统组件具有访问权限。手机通道不能证明人在场或防止中继。是否继续？", L"启用 AudioSwitch 自动提交", MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2) != IDYES) return ERROR_CANCELLED;
    BYTE* encrypted = nullptr; DWORD encryptedSize = 0; if (!Vault::Encrypt(record, &encrypted, &encryptedSize)) return ERROR_ENCRYPTION_FAILED;
    Bridge::Request request; request.intent = Bridge::Intent::Enroll; request.payloadBytes = encryptedSize; wcsncpy_s(request.sid, sid, _TRUNCATE);
    Bridge::Response response; const bool exchanged = Exchange(request, response, nullptr, encrypted);
    SecureZeroMemory(encrypted, encryptedSize); LocalFree(encrypted);
    if (!exchanged) return ERROR_SERVICE_NOT_ACTIVE;
    return response.result == Bridge::Result::Enrolled && response.error == 0 ? ERROR_SUCCESS : response.error ? response.error : ERROR_ACCESS_DENIED;
}
