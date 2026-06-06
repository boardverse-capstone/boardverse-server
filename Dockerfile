# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY BoardVerse.slnx ./
COPY BoardVerse.API/BoardVerse.API.csproj BoardVerse.API/
COPY BoardVerse.Core/BoardVerse.Core.csproj BoardVerse.Core/
COPY BoardVerse.Data/BoardVerse.Data.csproj BoardVerse.Data/
COPY BoardVerse.Services/BoardVerse.Services.csproj BoardVerse.Services/

RUN dotnet restore BoardVerse.API/BoardVerse.API.csproj

COPY . .
RUN dotnet publish BoardVerse.API/BoardVerse.API.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:$PORT
EXPOSE 10000
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BoardVerse.API.dll"]
