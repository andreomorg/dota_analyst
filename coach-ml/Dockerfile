# Optional GPU runtime for future experiments. Not built by the service profile.
FROM pytorch/pytorch@sha256:417bd75df6365104c283ea4c1651fb3530d9eb5a4c2fafa51943cff2a94e6385 AS training
WORKDIR /opt/coach-ml
COPY pyproject.toml /tmp/coach-ml-pyproject.toml
COPY constraints-training.txt /tmp/constraints-training.txt
RUN python -c "import subprocess, sys, tomllib; from pathlib import Path; deps = tomllib.loads(Path('/tmp/coach-ml-pyproject.toml').read_text())['project']['optional-dependencies']['train']; subprocess.check_call([sys.executable, '-m', 'pip', 'install', '--no-cache-dir', '-c', '/tmp/constraints-training.txt', *deps])"
COPY . .
RUN python -m pip install --no-cache-dir --no-deps .
ENV HF_HUB_OFFLINE=1 TRANSFORMERS_OFFLINE=1 TOKENIZERS_PARALLELISM=false PYTHONUNBUFFERED=1
ENV COACH_ML_RUN_STORE=/run-state/runs.sqlite3
ENTRYPOINT ["coach-ml"]
CMD ["--help"]

# CPU prediction models (death risk): scikit-learn only, no GPU, no network needed at run time.
FROM python:3.11-slim@sha256:e88e9763f943ec1834f992a4b51e0f24500486803e8bc534e5767af9ea65f6ce AS risk
WORKDIR /opt/coach-ml
COPY . .
RUN python -m pip install --no-cache-dir -c constraints-risk.txt ".[risk]" && useradd --system --uid 10001 coach
USER coach
ENV PYTHONUNBUFFERED=1
ENTRYPOINT ["coach-ml"]
CMD ["--help"]

# Lightweight HTTP service: standard library only, no GPU dependencies.
FROM python:3.11-slim@sha256:e88e9763f943ec1834f992a4b51e0f24500486803e8bc534e5767af9ea65f6ce AS service
WORKDIR /opt/coach-ml
COPY . .
RUN python -m pip install --no-cache-dir . && useradd --system --uid 10001 coach
USER coach
ENV PYTHONUNBUFFERED=1
EXPOSE 8090
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s CMD python -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8090/health', timeout=2)"
ENTRYPOINT ["coach-ml"]
CMD ["serve", "--host", "0.0.0.0", "--port", "8090"]
