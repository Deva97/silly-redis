#!/bin/bash

set -e

PROJECT_ROOT="/workspaces/silly-redis"
SERVER_LOG="/tmp/silly-redis-server.log"
TEST_LOG="/tmp/silly-redis-test.log"

# Kill any existing server process on port 6379
lsof -ti :6379 | xargs kill -9 2>/dev/null || true
sleep 1

echo "Starting server (output in: $SERVER_LOG)..."
cd "$PROJECT_ROOT"

# Start server in background with output to file
dotnet run --project ./src/sillyredis > "$SERVER_LOG" 2>&1 &
SERVER_PID=$!

echo "Server PID: $SERVER_PID"
echo "Waiting for server to start..."

# Wait for server to be ready (check if port is listening)
for i in {1..30}; do
    if lsof -Pi :6379 -sTCP:LISTEN -t >/dev/null 2>&1; then
        echo "✓ Server is ready!"
        break
    fi
    echo "  Waiting... ($i/30)"
    sleep 1
done

echo ""
echo "========================================"
echo "Running tests..."
echo "========================================"
echo ""

# Run tests in foreground
dotnet run --project ./tests/ServerTest 2>&1 | tee "$TEST_LOG"
TEST_EXIT=$?

echo ""
echo "========================================"
echo "Test Results: (exit code: $TEST_EXIT)"
echo "========================================"
echo ""

# Kill the server
echo "Stopping server (PID: $SERVER_PID)..."
kill $SERVER_PID 2>/dev/null || true

echo ""
echo "========================================"
echo "SERVER LOG:"
echo "========================================"
cat "$SERVER_LOG"
echo ""
echo "========================================"

exit $TEST_EXIT