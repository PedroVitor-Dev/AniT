"""Summarize local AniT JSONL traces without resolving media names or network endpoints."""
import argparse
import json
import math
import uuid
from collections import defaultdict
from pathlib import Path


def report(paths):
    sessions = defaultdict(list)
    malformed = 0
    for path in paths:
        for line in Path(path).read_text(encoding="utf-8-sig").splitlines():
            try:
                event = json.loads(line)
                if (not isinstance(event, dict) or not isinstance(event.get("Values"), dict)
                        or event.get("Kind") not in ("milestone", "sample", "event", "summary")
                        or not isinstance(event.get("Name"), str)
                        or not isinstance(event.get("Sequence"), int)
                        or not isinstance(event.get("ElapsedMs"), (int, float))
                        or not math.isfinite(event["ElapsedMs"])):
                    raise ValueError("Not a trace event")
                uuid.UUID(event["SessionId"])
                if any(not isinstance(v, (int, float)) or not math.isfinite(v) for v in event["Values"].values()):
                    raise ValueError("Non-numeric measurement")
                sessions[event["SessionId"]].append(event)
            except (ValueError, TypeError, KeyError, AttributeError):
                malformed += 1
    lines = ["# Diagnóstico local do streaming AniT", "",
             "Tempos em milissegundos desde o clique aceito. n/d significa não observado, não zero.",
             "O primeiro frame é um sinal aproximado do mpv (frame atual + saída configurada + playback-restart), não uma medição física do monitor.", ""]
    for session, events in sessions.items():
        events.sort(key=lambda e: e["Sequence"])
        points = {e["Name"]: e["ElapsedMs"] for e in events if e["Kind"] == "milestone"}
        ending = next((e for e in reversed(events) if e["Kind"] == "summary"), None)
        def timing(end, start="click"):
            return f"{max(0, points[end]-points[start]):.1f}" if end in points and start in points else "n/d"
        lines += [f"## Sessão {session[:8]}", "", f"Resultado: {ending['Name'] if ending else 'incompleta (sem resumo terminal)'}", "",
                  "| Etapa | Tempo (ms) |", "| --- | ---: |",
                  f"| Clique → episódio identificado | {timing('episode_resolved')} |",
                  f"| Restauração | {timing('restore_complete', 'restore_begin')} |",
                  f"| Configuração até iniciar motor | {timing('engine_started', 'restore_complete')} |",
                  f"| Clique → primeiro peer observado | {timing('first_peer_observed')} |",
                  f"| Clique → primeiros dados observados | {timing('first_data_observed')} |",
                  f"| Clique → cabeçalho verificado | {timing('header_verified')} |",
                  f"| Leitura do contêiner | {timing('container_probe_complete', 'container_probe_begin')} |",
                  f"| Clique → HTTP pronto | {timing('http_stream_ready')} |",
                  f"| Inicialização do processo → IPC | {timing('ipc_ready', 'process_start_requested')} |",
                  f"| Player solicitado → primeiro sinal de frame | {timing('first_video_frame_signal', 'player_requested')} |",
                  f"| Clique → primeiro sinal de frame | {timing('first_video_frame_signal')} |", ""]
        selected = next((e for e in events if e["Name"] == "file_selected"), None)
        lines.append(f"Cache verificado na seleção: {selected['Values'].get('cached_percent', 'n/d') if selected else 'n/d'}%.")
        values = ending["Values"] if ending and "first_video_frame_signal" in points else {}
        lines += [f"Engasgos após o primeiro sinal de frame: {values.get('rebuffer_count', 'n/d')}; duração total: {values.get('rebuffer_ms', 'n/d')} ms.", ""]
        samples = [e["Values"] for e in events if e["Kind"] == "sample" and e["Name"] == "player"]
        for key, label in [("cache_seconds", "Reserva de vídeo (s)"), ("output_dropped_frames", "Frames descartados na saída"),
                           ("decoder_dropped_frames", "Frames descartados no decodificador"), ("av_sync_seconds", "Desvio A/V (s)")]:
            numbers = [v[key] for v in samples if key in v]
            if numbers:
                lines.append(f"- {label}: mínimo {min(numbers):.3f}, máximo {max(numbers):.3f}.")
        lines += ["", "Marcos observados: " + ", ".join(points), ""]
    lines += [f"Sessões: {len(sessions)}. Linhas inválidas/inesperadas ignoradas: {malformed}.",
              "Compare apenas ensaios do mesmo arquivo, posição e condições registradas. Estes sinais não provam isoladamente a causa de um engasgo."]
    return "\n".join(lines) + "\n"


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("logs", nargs="+")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = report(args.logs)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(result, encoding="utf-8")
    else:
        print(result)
