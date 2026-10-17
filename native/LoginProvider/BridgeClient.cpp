#include <windows.h>
#include <bcrypt.h>
#include "BridgeProtocol.h"
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
bool VerifyBoundPhone(PCWSTR sid) {
    uint64_t phone = 0;
    if (!Bridge::ReadBinding(sid, &phone)) return true; // Unbound: preserve the existing manual-password lab mode.
    if (!phone) return false;
    Bridge::Request request;
    if (!ProcessIdToSessionId(GetCurrentProcessId(), &request.session) || wcsncpy_s(request.sid, sid, _TRUNCATE) != 0 || BCryptGenRandom(nullptr, request.nonce, sizeof(request.nonce), BCRYPT_USE_SYSTEM_PREFERRED_RNG) < 0) return false;
    // Identification only: a false pipe server must never obtain our impersonation privileges.
    auto pipe = CreateFileW(Bridge::PipeName, GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED | SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION, nullptr);
    if (pipe == INVALID_HANDLE_VALUE) return false;
    DWORD mode = PIPE_READMODE_MESSAGE;
    Bridge::Response response;
    const bool valid = SetNamedPipeHandleState(pipe, &mode, nullptr, nullptr) && IsServiceProcess(pipe) && Bridge::Transfer(pipe, &request, sizeof(request), true) && Bridge::Transfer(pipe, &response, sizeof(response), false, nullptr, 12000) && IsServiceProcess(pipe) && Bridge::ValidResponse(request, response, phone, GetTickCount64());
    uint64_t current = 0;
    const bool stillBound = Bridge::ReadBinding(sid, &current) && current == phone;
    CloseHandle(pipe); return valid && stillBound;
}
