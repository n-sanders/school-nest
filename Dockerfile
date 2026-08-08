# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/SchoolTracking/SchoolTracking.csproj src/SchoolTracking/
RUN dotnet restore src/SchoolTracking/SchoolTracking.csproj
COPY src/SchoolTracking/ src/SchoolTracking/
RUN dotnet publish src/SchoolTracking/SchoolTracking.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV Database__Path=/data/school.db
RUN mkdir -p /data
COPY --from=build /app/publish .
EXPOSE 8080
VOLUME ["/data"]
ENTRYPOINT ["dotnet", "SchoolTracking.dll"]
