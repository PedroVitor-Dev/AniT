# Verificação nativa do buffer adaptativo

Ferramenta de laboratório para Windows, fora do instalador. Usa o controlador real do AniT com mpv headless (`vo=null`, `ao=null`), uma fixture própria e o servidor HTTP do Online Anime Player. Não abre a biblioteca do usuário, não usa torrent público e só encerra o processo que ela iniciou.

Requer .NET 10, mpv do projeto e FFmpeg disponível para gerar a fixture. Use pasta nova, com espaço para aproximadamente 9 MB. Resultados existentes não são substituídos.

```powershell
New-Item -ItemType Directory artifacts/buffer-lab-novo
ffmpeg -f lavfi -i "testsrc2=size=320x180:rate=25" -f lavfi -i "sine=frequency=440:sample_rate=48000" -t 75 -c:v libx264 -preset ultrafast -b:v 800k -minrate 800k -maxrate 800k -bufsize 1600k -x264-params "nal-hrd=cbr:force-cfr=1" -pix_fmt yuv420p -c:a aac -b:a 64k -movflags +faststart artifacts/buffer-lab-novo/fixture.mp4
dotnet run --project tools/OnlineBufferProbe -c Release -- artifacts/buffer-lab-novo Player/MPV/mpv.exe
```

O produtor HTTP oferece inicialmente 400.000 B/s; de 15 a 27 s oferece 35.000 B/s; de 27 a 40 s aguarda sem entregar dados; depois recupera para 600.000 B/s. Isso limita as leituras do produtor, não esvazia os buffers TCP já preenchidos. O cenário não representa um limite idêntico de banda instantânea no socket.

Coleta 50 amostras de cache/opções. Pausa manualmente entre as amostras 44 e 47 e falha se o controlador desfizer essa pausa. O ciclo completo tem limite de 75 s. Usa reflexão somente no laboratório para conectar o player à instância headless; a integração de produção não depende desse mecanismo.

Saídas: `native-cache.json`, `native-playback.jsonl` e `native-result.json`. O primeiro sinal de frame é a aproximação já usada no diagnóstico do AniT, não captura da tela. A saída de vídeo e áudio é nula. O teste verifica integração/recuperação e preservação da pausa; não compara versões nem comprova fluidez em hardware ou torrents reais.
