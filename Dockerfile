FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .

# build API
WORKDIR /src/12
RUN dotnet publish MyApi.csproj -c Release -o /app/api

# build Bot
WORKDIR /src/MyBot
RUN dotnet publish -c Release -o /app/bot

# --------------------

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/api ./api
COPY --from=build /app/bot ./bot

# запуск обох процесів одночасно
CMD ["sh", "-c", "dotnet api/MyApi.dll & dotnet bot/MyBot.dll"]
