# Releases

Pushing an annotated `v*` tag triggers `.github/workflows/release.yml`:

1. Build the deterministic toolchain image from `docker/ci.Dockerfile`.
2. Push it to GHCR with the tag name and `latest`.
3. Create the GitHub Release with generated release notes.

Image:

```bash
docker pull ghcr.io/tkleisas/emd:v0.39.3
docker run --rm -v "$PWD:/work" -w /work ghcr.io/tkleisas/emd:v0.39.3 bash docker/ci-smoke.sh
```

The image contains the .NET SDK, Python co-simulation tools, ngspice, and
Renode. Mount the checkout as `/work` when running EMD or the smoke test.
