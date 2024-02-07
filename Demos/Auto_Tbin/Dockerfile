FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["Demos/Auto_Tbin/Auto_Tbin.csproj", "Demos/Auto_Tbin/"]
RUN dotnet restore "Demos/Auto_Tbin/Auto_Tbin.csproj"
COPY . .
WORKDIR "/src/Demos/Auto_Tbin"
RUN dotnet build "Auto_Tbin.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Auto_Tbin.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Auto_Tbin.dll"]
