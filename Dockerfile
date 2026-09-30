FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.401-alpine3.24 AS build

ARG TARGETARCH

WORKDIR /source

COPY global.json ./

COPY src/SingularFlow.Domain/SingularFlow.Domain.csproj \
    src/SingularFlow.Domain/

COPY src/SingularFlow.Application/SingularFlow.Application.csproj \
    src/SingularFlow.Application/

COPY src/SingularFlow.Infrastructure/SingularFlow.Infrastructure.csproj \
    src/SingularFlow.Infrastructure/

COPY src/SingularFlow.Api/SingularFlow.Api.csproj \
    src/SingularFlow.Api/

RUN dotnet restore \
    src/SingularFlow.Api/SingularFlow.Api.csproj \
    --arch $TARGETARCH

COPY src/ src/

RUN dotnet publish \
    src/SingularFlow.Api/SingularFlow.Api.csproj \
    --configuration Release \
    --arch $TARGETARCH \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-alpine3.24 AS runtime

RUN apk add --no-cache krb5-libs

WORKDIR /app

EXPOSE 8080

COPY --from=build /app/publish .

USER $APP_UID

HEALTHCHECK \
    --interval=10s \
    --timeout=3s \
    --start-period=10s \
    --retries=3 \
    CMD wget -q -O /dev/null \
        http://127.0.0.1:8080/health/ready \
        || exit 1

ENTRYPOINT ["dotnet", "SingularFlow.Api.dll"]