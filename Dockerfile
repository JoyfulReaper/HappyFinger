FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY . .

RUN apk add --no-cache \
    clang \
    build-base \
    zlib-dev


RUN dotnet restore HappyFinger.slnx

RUN dotnet publish HappyFinger/HappyFinger.csproj \
    --configuration Release \
    --runtime linux-musl-x64 \
    --self-contained true \
    /p:PublishAot=true \
    --no-restore \
    --output /app/publish


FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine AS final
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 79

ENTRYPOINT ["./HappyFinger"]
