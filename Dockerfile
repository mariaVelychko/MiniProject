# Один Dockerfile на всі сервіси: docker build --build-arg PROJECT=ScheduleService --build-arg DLL=ScheduleService.dll .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src
COPY . .
RUN dotnet publish "src/${PROJECT}" -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG DLL
ENV APP_DLL=${DLL}
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["sh", "-c", "dotnet /app/${APP_DLL}"]
