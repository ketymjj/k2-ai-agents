# Gera o arquivo .env a partir do .env.example com segredos aleatórios (Windows / PowerShell).
# Uso: powershell -ExecutionPolicy Bypass -File scripts/setup-env.ps1   (use -Force para sobrescrever um .env existente)
param([switch]$Force)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$example = Join-Path $root '.env.example'
$target = Join-Path $root '.env'
if ((Test-Path $target) -and -not $Force) {
  Write-Host '.env já existe. Use -Force para gerar um novo (o banco existente continua com as senhas antigas).'
  exit 1
}

function New-Secret([int]$bytes) {
  $buffer = New-Object byte[] $bytes
  [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
  return [Convert]::ToBase64String($buffer)
}
function New-Password {
  # Letras, números e símbolo: atende à política de senha (maiúscula, minúscula, número, 8+).
  $raw = (New-Secret 18) -replace '[+/=]', ''
  return 'K2-' + $raw.Substring(0, 16) + '9a'
}

$admin = New-Password
$values = @{
  'POSTGRES_PASSWORD'      = ((New-Secret 24) -replace '[+/=]', '')
  'JWT_SECRET'             = (New-Secret 48)
  'SECRETS_ENCRYPTION_KEY' = (New-Secret 32)
  'SEED_ADMIN_PASSWORD'    = $admin
  'SEED_DEMO_PASSWORD'     = 'Demo@2026'
}

$lines = Get-Content $example -Encoding utf8 | ForEach-Object {
  if ($_ -match '^([A-Z_]+)=' -and $values.ContainsKey($matches[1])) { "$($matches[1])=$($values[$matches[1]])" } else { $_ }
}
[IO.File]::WriteAllLines($target, $lines, (New-Object Text.UTF8Encoding $false))

Write-Host '.env criado com segredos aleatórios.'
Write-Host "Super Admin: admin@k2digital.com / $admin  (guarde agora; ela não é exibida de novo)"
Write-Host 'Usuários de demonstração: senha Demo@2026 (definida em SEED_DEMO_PASSWORD no .env).'
