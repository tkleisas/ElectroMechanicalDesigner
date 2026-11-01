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
      curl \
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

# Renode portable release, pinned by digest. Runtime is self-contained.
RUN curl -fsSL -o /tmp/renode.tar.gz \
      https://github.com/renode/renode/releases/download/v1.17.0/renode-1.17.0.linux-portable.tar.gz \
 && echo "4ba7c68b59e2447f188ef4b4b112fcccd2802582c460beedceb63b84e5605a5f  /tmp/renode.tar.gz" | sha256sum -c - \
 && mkdir -p /opt/renode \
 && tar -xzf /tmp/renode.tar.gz -C /opt/renode --strip-components=1 \
 && ln -s /opt/renode/renode /usr/local/bin/renode \
 && rm /tmp/renode.tar.gz

WORKDIR /work
CMD ["bash", "docker/ci-smoke.sh"]
