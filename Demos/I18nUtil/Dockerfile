FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["Demos/I18nUtil/I18nUtil.csproj", "Demos/I18nUtil/"]
RUN dotnet restore "Demos/I18nUtil/I18nUtil.csproj"
COPY . .
WORKDIR "/src/Demos/I18nUtil"
RUN dotnet build "I18nUtil.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "I18nUtil.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "I18nUtil.dll"]
