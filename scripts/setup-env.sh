#!/usr/bin/env sh
# Gera o arquivo .env a partir do .env.example com segredos aleatórios (Linux / macOS / Git Bash).
# Uso: sh scripts/setup-env.sh   (use --force para sobrescrever um .env existente)
set -eu

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
EXAMPLE="$ROOT/.env.example"
TARGET="$ROOT/.env"

if [ -f "$TARGET" ] && [ "${1:-}" != "--force" ]; then
  echo ".env já existe. Use --force para gerar um novo (o banco existente continua com as senhas antigas)."
  exit 1
fi

secret() { openssl rand -base64 "$1" | tr -d '\n'; }
alnum() { openssl rand -base64 "$1" | tr -dc 'A-Za-z0-9' | cut -c1-"$2"; }

ADMIN="K2-$(alnum 24 16)9a"
POSTGRES_PASSWORD="$(alnum 32 32)"
JWT_SECRET="$(secret 48)"
SECRETS_ENCRYPTION_KEY="$(secret 32)"

# Substitui só as chaves geradas; o resto do exemplo é mantido (| como delimitador: Base64 não contém |).
sed \
  -e "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=$POSTGRES_PASSWORD|" \
  -e "s|^JWT_SECRET=.*|JWT_SECRET=$JWT_SECRET|" \
  -e "s|^SECRETS_ENCRYPTION_KEY=.*|SECRETS_ENCRYPTION_KEY=$SECRETS_ENCRYPTION_KEY|" \
  -e "s|^SEED_ADMIN_PASSWORD=.*|SEED_ADMIN_PASSWORD=$ADMIN|" \
  -e "s|^SEED_DEMO_PASSWORD=.*|SEED_DEMO_PASSWORD=Demo@2026|" \
  "$EXAMPLE" > "$TARGET"

echo ".env criado com segredos aleatórios."
echo "Super Admin: admin@k2digital.com / $ADMIN  (guarde agora; ela não é exibida de novo)"
echo "Usuários de demonstração: senha Demo@2026 (definida em SEED_DEMO_PASSWORD no .env)."
