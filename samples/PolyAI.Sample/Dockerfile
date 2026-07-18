FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["src/PolyAI/PolyAI.csproj", "src/PolyAI/"]
COPY ["samples/PolyAI.Sample/PolyAI.Sample.csproj", "samples/PolyAI.Sample/"]
RUN dotnet restore "samples/PolyAI.Sample/PolyAI.Sample.csproj"

COPY . .
RUN dotnet publish "samples/PolyAI.Sample/PolyAI.Sample.csproj" -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /app

RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

COPY --from=build /app .

ENTRYPOINT ["dotnet", "PolyAI.Sample.dll"]
