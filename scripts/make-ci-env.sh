#!/usr/bin/env bash
# Writes a throwaway .env for CI from .env.example, replacing every password and key with a random
# value generated for this run only. The values are masked in the Actions log and never stored.
set -euo pipefail

cd "$(dirname "$0")/.."

random() { openssl rand -hex 12; }

# SQL Server and Identity need upper case, lower case, a digit and a symbol; the prefix guarantees them.
db_password="Aa1!$(random)"
servicebus_sql_password="Aa1!$(random)"
jwt_key="$(random)$(random)"
admin_password="Aa1!$(random)"
grafana_password="Aa1!$(random)"

for value in "$db_password" "$servicebus_sql_password" "$jwt_key" "$admin_password" "$grafana_password"; do
    echo "::add-mask::$value"
done

sed \
    -e "s|^DB_SA_PASSWORD=.*|DB_SA_PASSWORD=$db_password|" \
    -e "s|^SERVICEBUS_SQL_PASSWORD=.*|SERVICEBUS_SQL_PASSWORD=$servicebus_sql_password|" \
    -e "s|^JWT_KEY=.*|JWT_KEY=$jwt_key|" \
    -e "s|^SEED_ADMIN_PASSWORD=.*|SEED_ADMIN_PASSWORD=$admin_password|" \
    -e "s|^GRAFANA_ADMIN_PASSWORD=.*|GRAFANA_ADMIN_PASSWORD=$grafana_password|" \
    .env.example > .env

echo "Wrote .env with run-specific credentials."
