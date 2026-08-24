# syntax=docker/dockerfile:1
ARG GAME_CI_BASE=unityci/base:ubuntu-3.2.2@sha256:076fe28aa9863a2466c69d4dce5af51e25988b31e4d9ed110f4025f6fd2f98d3

FROM ${GAME_CI_BASE} AS extractor

ARG UNITY_VERSION=5.6.4f1
ARG UNITY_CHANGESET=ac7086b8d112
ARG UNITY_INSTALLER_SHA256=04c42eebf7a037bb06f61449991125a7e24cea2219e16d1e276926878989ab13

RUN apt-get update \
    && apt-get install -y --no-install-recommends p7zip-full \
    && rm -rf /var/lib/apt/lists/* \
    && curl -fL \
        "https://download.unity3d.com/download_unity/${UNITY_CHANGESET}/Windows64EditorInstaller/UnitySetup64-${UNITY_VERSION}.exe" \
        -o /tmp/UnitySetup.exe \
    && echo "${UNITY_INSTALLER_SHA256}  /tmp/UnitySetup.exe" | sha256sum -c - \
    && mkdir -p /opt/unity \
    && 7z x -y -o/opt/unity /tmp/UnitySetup.exe 'Editor/*' \
    && test -f /opt/unity/Editor/Unity.exe \
    && rm -f /tmp/UnitySetup.exe

FROM ${GAME_CI_BASE}

LABEL org.opencontainers.image.title="MU3 Unity AssetBundle Builder" \
      org.opencontainers.image.version="5.6.4f1"

ENV WINEARCH=win64 \
    WINEPREFIX=/opt/wine \
    WINEDEBUG=-all \
    WINEDLLOVERRIDES=winemenubuilder.exe=d

RUN dpkg --add-architecture i386 \
    && apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        fonts-liberation \
        python3 \
        wine \
        wine32 \
        wine64 \
        winbind \
        xauth \
        xvfb \
    && rm -rf /var/lib/apt/lists/*

COPY --from=extractor /opt/unity /opt/unity
COPY --chmod=755 activate_personal.py /usr/local/bin/unity-personal-activate
COPY --chmod=755 patch_mono_rosetta.py /usr/local/bin/unity-mono-rosetta-patch

RUN mkdir -p "$WINEPREFIX" \
    && xvfb-run -a wineboot --init \
    && wineserver -w

COPY --chmod=755 unity-entrypoint.sh /usr/local/bin/unity-entrypoint

WORKDIR /workspace
ENTRYPOINT ["/usr/local/bin/unity-entrypoint"]
