FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["NexAuthDb/NexAuthDb.csproj", "NexAuthDb/"]
RUN dotnet restore "NexAuthDb/NexAuthDb.csproj"
COPY . .
WORKDIR "/src/NexAuthDb"
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "NexAuthDb.dll"]
