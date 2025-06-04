# Dockerfile for combined API and TelegramBot
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .

# Build API (MyApi.csproj in /12)
WORKDIR /src/12
RUN dotnet restore MyApi.csproj
RUN dotnet publish MyApi.csproj -c Release -o /app/api

# Build Bot (MyBot.csproj in /MyBot)
WORKDIR /src/MyBot
RUN dotnet restore
RUN dotnet publish -c Release -o /app/bot

# Final image
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/api ./api
COPY --from=build /app/bot ./bot

# Run both API and Bot concurrently
CMD ["sh", "-c", "dotnet api/MyApi.dll & dotnet bot/MyBot.dll"]
