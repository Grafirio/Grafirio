#!/usr/bin/env bash
# Imajin varsayilan girisi.
#
# docker-compose bu servisi "migrate && start_ai_consumer & daphne" seklinde
# baslatiyordu; Container Apps compose'un command satirini kullanmadigi icin
# Azure'da yalnizca daphne ayaga kalkiyor, RabbitMQ tuketicisi hic calismiyordu.
# Sonuc: sorular kuyruga dusuyor ama kimse islemiyor, istemci sonsuza kadar
# "not_found" gorup bekliyordu. Baslatma mantigi bu yuzden imajin icinde.
set -euo pipefail

python manage.py migrate --noinput

python manage.py start_ai_consumer &
CONSUMER_PID=$!

daphne -b 0.0.0.0 -p 8000 config.asgi:application &
DAPHNE_PID=$!

# Ikisi de zorunlu: biri olurse container yarim calismaya devam etmesin,
# dussun ki platform yeniden baslatsin ve durum loglarda gorunur olsun.
wait -n "$CONSUMER_PID" "$DAPHNE_PID"
EXIT_CODE=$?
echo "entrypoint: alt sureclerden biri ${EXIT_CODE} koduyla sonlandi, container kapatiliyor" >&2
exit "${EXIT_CODE}"
