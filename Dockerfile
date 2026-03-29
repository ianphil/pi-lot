FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY llm-svc.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish llm-svc.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:5100
EXPOSE 5100
ENTRYPOINT ["dotnet", "llm-svc.dll"]
