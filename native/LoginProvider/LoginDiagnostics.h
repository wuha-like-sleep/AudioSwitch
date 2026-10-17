#pragma once
#include "BridgeProtocol.h"
// Diagnostic metadata is never consulted for authorization. No identity, address,
// password, nonce or serialized credential is written here.
namespace LoginDiagnostics {
struct Record { DWORD version = 1, result = 0, error = 0, substatus = 0; ULONGLONG utc = 0; };
static_assert(sizeof(Record) == 24, "Stable diagnostic layout");
inline void Write(PCWSTR sid, PCWSTR stage, DWORD result, DWORD error = 0, DWORD substatus = 0) {
    uint64_t phone = 0;
    if (!Bridge::ReadBinding(sid, &phone) || !phone) return;
    wchar_t path[256]{};
    if (swprintf_s(path, L"%s%s", Bridge::BindingRoot, sid) < 0) return;
    HKEY key = nullptr;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, path, 0, KEY_SET_VALUE | KEY_WOW64_64KEY, &key) != ERROR_SUCCESS) return;
    FILETIME time{}; GetSystemTimeAsFileTime(&time);
    Record record; record.result = result; record.error = error; record.substatus = substatus;
    record.utc = (static_cast<ULONGLONG>(time.dwHighDateTime) << 32) | time.dwLowDateTime;
    RegSetValueExW(key, stage, 0, REG_BINARY, reinterpret_cast<const BYTE*>(&record), sizeof(record));
    RegCloseKey(key);
}
}
