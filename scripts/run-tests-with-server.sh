cd /workspaces/silly-redis
dotnet run --project /workspaces/silly-redis/src/sillyredis &
SERVER_PID=$!
sleep 2
dotnet run /workspaces/silly-redis/tests/ServerTest
kill $SERVER_PID