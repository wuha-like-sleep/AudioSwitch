#pragma once
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <cwchar>
namespace Bridge {
inline constexpr wchar_t ServiceName[] = L"AudioSwitchLoginBridge";
inline constexpr wchar_t PipeName[] = L"\\\\.\\pipe\\AudioSwitch.LoginBridge.v3";
inline constexpr wchar_t BindingRoot[] = L"SOFTWARE\\AudioSwitch\\LoginBridge\\Bindings\\";
inline constexpr uint32_t Magic = 0x41534231, Version = 3;
enum class Result : uint32_t { Denied = 0, Verified = 1, NotBound = 2, PhoneUnavailable = 3, SessionMismatch = 4, Waiting = 5, Enrolled = 6, AlreadyTried = 7 };
enum class Intent : uint32_t { Manual = 0, AutoProbe = 1, AutoCommit = 2, Enroll = 3, ServiceProbe = 4 };
enum class PhoneStage : DWORD { None = 0, Adapter = 1, Pairing = 2, Socket = 3, Authentication = 4, Encryption = 5, Events = 6, Connect = 7, Wait = 8, Peer = 9, Cancelled = 10 };
inline bool CanArmAbsence(DWORD error, PhoneStage stage) {
    // Only radio reachability failures, never local configuration/authentication errors.
    return (stage == PhoneStage::Connect || stage == PhoneStage::Wait) &&
        (error == 10060 || error == 10064 || error == 10051 || error == 10054 || error == 10101);
}
struct Request { uint32_t magic = Magic, version = Version, size = sizeof(Request), session = 0; Intent intent = Intent::Manual; uint32_t payloadBytes = 0; BYTE nonce[32]{}, proof[32]{}; wchar_t sid[184]{}; };
struct Response { uint32_t magic = Magic, version = Version, size = sizeof(Response); Result result = Result::Denied; uint32_t session = 0, error = 0; BYTE nonce[32]{}; uint64_t phone = 0, completed = 0; };
static_assert(sizeof(Request) == 456 && sizeof(Response) == 72, "Fixed IPC layout across supported architectures");
inline bool ValidRequest(const Request& request) {
    BYTE aggregate = 0; for (auto value : request.nonce) aggregate |= value;
    return request.magic == Magic && request.version == Version && request.size == sizeof(Request) && aggregate && request.sid[0] && wmemchr(request.sid, 0, sizeof(request.sid) / sizeof(request.sid[0])) && request.session != 0 && static_cast<uint32_t>(request.intent) <= 4 && (request.intent == Intent::Enroll ? request.payloadBytes > 0 && request.payloadBytes <= 16384 : request.payloadBytes == 0);
}
inline bool ValidResponse(const Request& request, const Response& response, uint64_t phone, uint64_t now) {
    return ValidRequest(request) && response.magic == Magic && response.version == Version && response.size == sizeof(Response) && response.result == Result::Verified && response.error == 0 && response.session == request.session && response.phone == phone && phone && !memcmp(request.nonce, response.nonce, sizeof(request.nonce)) && response.completed <= now && now - response.completed <= 2000;
}
inline bool ReadBinding(PCWSTR sid, uint64_t* phone) {
    *phone = 0;
    if (!sid || wcslen(sid) > 183 || wcsncmp(sid, L"S-1-", 4) != 0 || wcspbrk(sid, L"\\/")) return false;
    wchar_t path[256]{}; if (swprintf_s(path, L"%s%s", BindingRoot, sid) < 0) return false;
    HKEY key = nullptr;
    const auto opened = RegOpenKeyExW(HKEY_LOCAL_MACHINE, path, 0, KEY_QUERY_VALUE | KEY_WOW64_64KEY, &key);
    if (opened == ERROR_FILE_NOT_FOUND || opened == ERROR_PATH_NOT_FOUND) return false;
    if (opened != ERROR_SUCCESS) return true;
    wchar_t address[32]{}; DWORD bytes = sizeof(address), type = 0;
    const auto error = RegGetValueW(key, nullptr, L"Phone", RRF_RT_REG_SZ, &type, address, &bytes);
    RegCloseKey(key);
    // An existing but inaccessible/malformed binding remains enabled and fails closed.
    if (error != ERROR_SUCCESS || bytes < 12 * sizeof(wchar_t) || bytes > sizeof(address) || bytes % sizeof(wchar_t) || wcsnlen_s(address, sizeof(address) / sizeof(address[0])) != 12) return true;
    // Registry writers may include different counts of trailing NULs. Accept
    // only the same twelve hex digits and zero padding, never a suffix.
    for (size_t index = 13; index < bytes / sizeof(wchar_t); ++index) if (address[index]) return true;
    uint64_t parsed = 0;
    for (size_t index = 0; index < 12; ++index) {
        wchar_t c = address[index]; unsigned digit;
        if (c >= L'0' && c <= L'9') digit = c - L'0'; else if (c >= L'A' && c <= L'F') digit = c - L'A' + 10; else if (c >= L'a' && c <= L'f') digit = c - L'a' + 10; else return true;
        parsed = (parsed << 4) | digit;
    }
    *phone = parsed; return true;
}
inline bool Transfer(HANDLE pipe, void* data, DWORD length, bool write, HANDLE stop = nullptr, DWORD timeout = 2000) {
    OVERLAPPED operation{}; operation.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr); if (!operation.hEvent) return false;
    DWORD transferred = 0; BOOL ok = write ? WriteFile(pipe, data, length, &transferred, &operation) : ReadFile(pipe, data, length, &transferred, &operation);
    if (!ok && GetLastError() == ERROR_IO_PENDING) {
        HANDLE waits[] = {operation.hEvent, stop};
        const auto wait = WaitForMultipleObjects(stop ? 2 : 1, waits, FALSE, timeout);
        if (wait == WAIT_OBJECT_0) ok = GetOverlappedResult(pipe, &operation, &transferred, FALSE);
        else { CancelIoEx(pipe, &operation); GetOverlappedResult(pipe, &operation, &transferred, TRUE); ok = FALSE; }
    }
    CloseHandle(operation.hEvent); return ok && transferred == length;
}
}
