# LuaLS integration fixture

The live integration tests are opt-in and drive the real LuaLS executable. LuaLS is not bundled:
this repository provides no archive, so the tier stays dormant until a developer downloads a
[LuaLS release](https://github.com/LuaLS/lua-language-server/releases) and points
`NICKELONY_LUA_LANGUAGE_SERVER_ARCHIVE` at it.

The archive must contain `bin/lua-language-server.exe` on Windows or
`bin/lua-language-server` on Unix-like systems. When the variable is unset, or the extracted archive
is missing that executable, the tests skip explicitly with a prerequisite message; they are not
deleted or silently treated as passing.
