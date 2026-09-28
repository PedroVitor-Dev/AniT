# MPC-HC integrado

O AniT 1.0.2 homologa o pacote portátil **MPC-HC 2.8.2 x64** de `clsid2/mpc-hc`.

## Desenvolvimento

Extraia o pacote oficial em `Player/MPC-HC/` para que exista:

```text
Player\MPC-HC\mpc-hc64.exe
```

Binários não entram no Git. Para preparar uma árvore limpa de publicação, use `scripts/Get-MpcHc.ps1`; o script baixa somente o release configurado e interrompe se o hash não corresponder.

- Release: <https://github.com/clsid2/mpc-hc/releases/tag/2.8.2>
- Arquivo: `MPC-HC.2.8.2.x64.zip`
- SHA-256: `EEFEE5AC29FC33E6031E34E0E163E157212D272C1BCF576149D40C0C7ABB32F4`

## Publicação

`scripts/Build-Installer.ps1` inclui a árvore homologada em `Player/MPC-HC` ao lado do `AniT.exe`. O smoke test deve confirmar abertura, posição, encerramento e preservação do `COPYING.txt`.

O MPC-HC é GPL-3.0 e conserva avisos/licenças do pacote upstream. Consulte [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).
