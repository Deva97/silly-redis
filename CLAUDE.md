# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

**Run the server:**
```bash
dotnet run --project ./src/sillyredis
```

**Build only:**
```bash
dotnet build src/sillyredis/sillyredis.csproj
```

**Run integration tests (requires a running server):**
```bash
dotnet run --project ./tests/ServerTest
```

**Run server + tests in one shot:**
```bash
bash scripts/run-tests-with-server.sh
```

The test script starts the server on port 6379, waits for it to be ready, runs the test suite, then kills the server.

## Architecture

The server is a single-process TCP server bound to `127.0.0.1:6379`.

**Request lifecycle:**
1. `Server.cs` accepts TCP clients in a loop, dispatching each to `HandleClientAsync` (fire-and-forget — concurrency via `Task`, not threads).
2. `ReadRequestAsync` accumulates bytes until `IsCompleteRespMessage` detects a full RESP array (counts `*` and `$` lines to infer expected `\r\n` count).
3. `RESProtocol.ParseResp` strips RESP framing (`*N`, `$N` lines) and returns raw string args.
4. The `Response` function in `Program.cs` dispatches on `args[0]` (the command name) and returns a RESP-encoded string.
5. The response is written back to the client stream.

**Concurrency model:**
- Each key has its own lock object, stored in `keyLocks` (a `ConcurrentDictionary<string, object>`). All read-modify-write operations on a key acquire `GetKeyLock(key)` before touching registry state.
- `BLPOP` blocks the handler thread (via `Thread.Sleep` polling loop) while holding no locks — it acquires the key lock only briefly per poll iteration.

**Key data structures:**
- `ConcurrentDictionary<string, CachedValue<object>> registry` — the main store. Values are `List<string>` for lists or `string` for key-value.
- `CachedValue<T>` wraps a value + `DateTime Expiry`. `DateTime.MaxValue` means no expiry.
- `RedisList` owns all list command logic and takes the registry + `GetKeyLock` factory as constructor args.

**RESP encoding (`RESProtocol.cs`):**
- `EncodeSimpleString` → `+...\r\n`
- `EncodeBulkString` → `$N\r\n...\r\n`
- `EncodeNullBulkString` → `$-1\r\n`
- `EncodeArray` → `*N\r\n` + bulk strings for each element
- `EncodeInteger` → `:N\r\n`
- `EncodeError` → `-...\r\n`

**SET semantics (non-standard):** `SET` on a key that already exists and has not expired returns `-ERR key already exists and is not expired`. This differs from standard Redis (which overwrites). Only expired keys can be overwritten.

**BLPOP:** `BLPOP key [key ...] timeout` — last arg is always the timeout (float, 0 = block indefinitely). Returns a 2-element array `["key", "value"]` matching Redis spec. Polls every 100ms (capped to remaining timeout). Respects `CancellationToken` for clean server shutdown.

## Test Structure

Tests in `tests/ServerTest/Program.cs` are integration tests that connect over TCP. They are split into `RunSequentialTests` and `RunConcurrencyTests`. There is no unit test framework — assertions use a local `Assert(bool, string)` helper that prints `[PASS]`/`[FAIL]` and exits with code 1 on any failure.
