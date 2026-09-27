# MPC-HC integrado

O AniT homologou o pacote portátil `MPC-HC 2.8.2 x64` mantido pelo projeto `clsid2/mpc-hc`.

Durante o desenvolvimento, extraia o pacote oficial em `Player/MPC-HC/`, com `mpc-hc64.exe` nessa pasta. O diretório de binários não entra no Git; o processo de publicação deverá copiá-lo para `Player/MPC-HC` ao lado do `AniT.exe`.

Fonte: https://github.com/clsid2/mpc-hc/releases/tag/2.8.2

Arquivo oficial: `MPC-HC.2.8.2.x64.zip`

SHA-256 do arquivo: `EEFEE5AC29FC33E6031E34E0E163E157212D272C1BCF576149D40C0C7ABB32F4`

Para preparar uma árvore limpa de publicação, execute `scripts/Get-MpcHc.ps1` apontando para um diretório vazio. O script baixa somente o release oficial e interrompe a operação se o hash não corresponder.

O MPC-HC é distribuído sob GPL-3.0. A distribuição do AniT deverá incluir os avisos e a licença fornecidos pelo pacote original.
