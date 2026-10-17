#pragma once
#include <windows.h>
// No credentials are sent over the pipe. Only the bound phone's fresh channel result is requested.
bool VerifyBoundPhone(PCWSTR sid);
