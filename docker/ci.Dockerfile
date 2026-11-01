# Deterministic Linux CI image. Pinned .NET SDK digest and Python tool versions.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:9c7b6b263d9d4c2e4718e16dae6d75cd8f4b5ccf206f1e7d48a98eb429981b03

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      ca-certificates \
      clang \
      git \
      ngspice \
      python3 \
      python3-pip \
      zlib1g-dev \
 && rm -rf /var/lib/apt/lists/*

RUN python3 -m pip install --break-system-packages --no-cache-dir \
      pythonfmu==0.7.0 \
      fmpy==0.3.32 \
      omsimulator==3.0.0.post198

WORKDIR /work
CMD ["bash", "docker/ci-smoke.sh"]
