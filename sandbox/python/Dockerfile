FROM python:3.12-slim-bookworm

RUN useradd --create-home --uid 1000 --shell /usr/sbin/nologin sandbox \
 && mkdir -p /app /tmp \
 && chown -R sandbox:sandbox /app /tmp

WORKDIR /app
COPY requirements.txt /app/requirements.txt
RUN pip install --no-cache-dir -r /app/requirements.txt
COPY server.py delegat_sdk.py sandbox_limits.py /app/

USER sandbox
ENV PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    SANDBOX_PORT=8080 \
    OMP_NUM_THREADS=1 \
    MKL_NUM_THREADS=1 \
    OPENBLAS_NUM_THREADS=1 \
    NUMEXPR_NUM_THREADS=1 \
    VECLIB_MAXIMUM_THREADS=1 \
    BLIS_NUM_THREADS=1
EXPOSE 8080
CMD ["python", "/app/server.py"]
