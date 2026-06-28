# syntax=docker/dockerfile:1
#
# Build image for the Zen managed-vs-native comparison.
#
#   Stage 1 (native):  compiles the Rust cdylib (libzen_native.so).
#   Stage 2 (dev):     .NET 8 SDK + the native artifact at a stable path.
#
# Source is NOT copied in; it is bind-mounted at runtime so C# iteration does
# not require rebuilding the image. Only a change to the Rust source rebuilds.
#
#   docker build -t zen-dev -f docker/Dockerfile .
#   docker run --rm -v "$PWD":/work -w /work zen-dev <cmd>
#
# Typical <cmd> values:
#   dotnet build Zen.sln -c Release
#   dotnet test  src/Zen.Tests/Zen.Tests.csproj -c Release
#   dotnet run   -c Release --project src/Zen.Benchmarks

ARG DOTNET_SDK=mcr.microsoft.com/dotnet/sdk:8.0
ARG RUST_IMAGE=rust:1.94-bookworm

# ---------- stage 1: native cdylib ----------
FROM ${RUST_IMAGE} AS native
WORKDIR /native
# Copy only the manifest + source so dependency builds cache independently.
COPY native/zen-native/Cargo.toml ./Cargo.toml
COPY native/zen-native/src ./src
RUN --mount=type=cache,target=/usr/local/cargo/registry \
    --mount=type=cache,target=/native/target \
    cargo build --release && \
    cp target/release/libzen_native.so /libzen_native.so

# ---------- stage 2: development image ----------
FROM ${DOTNET_SDK} AS dev
ARG DEBIAN_FRONTEND=noninteractive

# Bring the native library to a stable, well-known path.
COPY --from=native /libzen_native.so /opt/zen/libzen_native.so

# Persist NuGet and cargo caches across runs for faster restores.
ENV ZEN_NATIVE_LIB=/opt/zen/libzen_native.so
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=1
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0

WORKDIR /work

# Default to a shell so `docker run zen-dev` drops into bash.
CMD ["bash"]
