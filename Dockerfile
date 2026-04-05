FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/llm-svc/llm-svc.csproj src/llm-svc/
COPY src/CopilotLlm/CopilotLlm.csproj src/CopilotLlm/
RUN dotnet restore src/llm-svc/llm-svc.csproj
COPY src/ src/
RUN dotnet publish src/llm-svc/llm-svc.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:5100
EXPOSE 5100
ENTRYPOINT ["dotnet", "llm-svc.dll"]
