"""Reproducible, offline motor comparison. Each trial gets a fresh client AND seed."""
import argparse
import datetime
import json
import os
import math
from pathlib import Path
import platform
import statistics
import subprocess
import sys
import threading
import queue


def read_ready(process):
    lines = queue.Queue()
    def read():
        lines.put(process.stdout.readline())
    threading.Thread(target=read, daemon=True).start()
    try:
        line = lines.get(timeout=30)
    except queue.Empty:
        raise TimeoutError("Seed did not become ready within 30 seconds")
    if not line:
        raise RuntimeError("Seed exited before ready: " + process.stderr.read())
    result = json.loads(line)
    if not result.get("ready") or result.get("wire_bytes_per_second") != 1024 * 1024:
        raise RuntimeError("Seed must expose the external 1 MiB/s TCP relay; rebuild the benchmark")
    return result


def validate_result(row):
    """Reject incomplete/incorrect trials rather than showing them as fast successes."""
    expected = [0, 12 * 1024 * 1024, 4 * 1024 * 1024, 0]
    reads = row.get("reads", [])
    if len(reads) != len(expected):
        raise ValueError("Incomplete range verification")
    previous = 0
    for read, offset in zip(reads, expected):
        if read.get("offset") != offset or read.get("bytes") != 65536 or read.get("verified") is not True:
            raise ValueError("Unexpected or unverified range")
        for key in ("ready_ms", "wait_ms"):
            value = read.get(key)
            if not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
                raise ValueError("Invalid read timing")
        if read["ready_ms"] < previous or read["wait_ms"] > read["ready_ms"] + 1:
            raise ValueError("Inconsistent read timeline")
        previous = read["ready_ms"]
    if row.get("unselected_pieces") != 0:
        raise ValueError("An unselected episode received verified pieces")
    if row.get("warm") is not (row["condition"] == "warm"):
        raise ValueError("Unexpected cache condition")
    if row["condition"] == "warm" and row.get("received_bytes") != 0:
        raise ValueError("Warm trial unexpectedly downloaded content")


def summary(results):
    lines = ["# Comparação controlada dos motores torrent", "",
             "Seed MonoTorrent novo a cada tentativa; loopback TCP; relay TCP externo limitado a 1 MiB/s, sem rajada inicial; "
             "3 arquivos sintéticos de 16 MiB; somente o segundo selecionado. Metadados locais, sem DHT/trackers.", "",
             "Tempos em ms. Inicial = intervalo desde ativação até 64 KiB verificados do início. "
             "Busca = espera pelos mesmos 64 KiB em 75% e 25% do arquivo. Não há decodificação de vídeo.", "",
             "| Motor | Peça | Cache | Ensaios OK/total | Inicial mediana / pior | Busca 75% mediana | Busca 25% mediana | Bytes recebidos mediana |",
             "| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: |"]
    groups = {}
    for row in results:
        groups.setdefault((row["engine"], row["piece_bytes"], row["condition"]), []).append(row)
    for (engine, piece, condition), rows in sorted(groups.items()):
        good = [row for row in rows if row.get("ok")]
        if not good:
            lines.append(f"| {engine} | {piece} | {condition} | 0/{len(rows)} | n/d | n/d | n/d | n/d |")
            continue
        initial = [r["reads"][0]["ready_ms"] for r in good]
        seeks = [[r["reads"][i]["wait_ms"] for r in good] for i in (1, 2)]
        lines.append(f"| {engine} | {piece} | {condition} | {len(good)}/{len(rows)} | "
                     f"{statistics.median(initial):.1f} / {max(initial):.1f} | "
                     f"{statistics.median(seeks[0]):.1f} | {statistics.median(seeks[1]):.1f} | "
                     f"{statistics.median(r['received_bytes'] for r in good):.0f} |")
    lines += ["", "Falhas permanecem no JSON bruto. Valores ausentes não representam zero. "
              "Comparação das integrações de streaming usadas na bancada: StreamProvider do MonoTorrent e "
              "set_piece_deadline/read_piece do libtorrent. Não mede primeiro quadro, rede pública, "
              "rebuffer de vídeo, estabilidade de longa duração ou ganho no torrent do usuário.", ""]
    for row in results:
        if not row.get("ok"):
            lines.append(f"- Falha {row['engine']}/{row['piece_bytes']}/{row['condition']}/{row['trial']}: {row['error']}")
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--libs", type=Path, required=True, help="Directory containing the isolated libtorrent wheel")
    parser.add_argument("--output", type=Path, required=True, help="New directory; existing results are never overwritten")
    parser.add_argument("--repetitions", type=int, default=3)
    args = parser.parse_args()
    if not 1 <= args.repetitions <= 10:
        parser.error("repetitions must be between 1 and 10")
    args.output.mkdir(parents=True, exist_ok=False)
    here = Path(__file__).resolve().parent
    dll = here / "bin" / "Release" / "net10.0" / "TorrentEngineBench.dll"
    if not dll.exists():
        parser.error("Build TorrentEngineBench in Release first")
    env = os.environ.copy()
    env["PYTHONPATH"] = str(args.libs.resolve())
    results = []
    for piece in (65536, 4194304):
        fixture = args.output / f"fixture-{piece}"
        subprocess.run([args.dotnet, str(dll), "fixture", str(fixture), str(piece)], check=True, capture_output=True, text=True, timeout=60)
        for condition in ("cold", "warm"):
            for trial in range(args.repetitions):
                # Alternate execution order to reduce a systematic cache/order bias.
                engines = ("MonoTorrent", "libtorrent") if trial % 2 == 0 else ("libtorrent", "MonoTorrent")
                for engine in engines:
                    destination = args.output / f"{engine}-{piece}-{condition}-{trial}"
                    seed = subprocess.Popen([args.dotnet, str(dll), "seed", str(fixture)], stdin=subprocess.PIPE,
                                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
                    row = {"engine": engine, "piece_bytes": piece, "condition": condition, "trial": trial, "ok": False}
                    try:
                        port = str(read_ready(seed)["port"])
                        command = ([args.dotnet, str(dll), "mono", str(fixture), str(destination), port, condition]
                                   if engine == "MonoTorrent" else [sys.executable, str(here / "libtorrent_probe.py"), str(fixture), str(destination), port, condition])
                        output = subprocess.run(command, capture_output=True, text=True, env=env, timeout=75, check=True)
                        (args.output / (destination.name + ".stderr.txt")).write_text(output.stderr, encoding="utf-8")
                        row.update(json.loads(output.stdout))
                        validate_result(row)
                        row["ok"] = True
                    except (Exception, KeyboardInterrupt) as error:
                        row["error"] = str(error)
                        if isinstance(error, subprocess.CalledProcessError):
                            row["stderr"] = error.stderr
                            row["error"] = ("Timeout de 60 s ao aguardar intervalo verificado."
                                            if "Trial exceeded 60 seconds" in error.stderr
                                            else f"Cliente encerrou com código {error.returncode}; veja stderr.")
                        if isinstance(error, KeyboardInterrupt):
                            raise
                    finally:
                        if seed.poll() is None:
                            seed.stdin.write("stop\n"); seed.stdin.flush()
                        try:
                            seed.communicate(timeout=10)
                        except subprocess.TimeoutExpired:
                            seed.kill(); seed.communicate()
                    results.append(row)
                    metadata = {"schema": 1, "utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                                "platform": platform.platform(), "python": platform.python_version(), "results": results}
                    (args.output / "results.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
                    (args.output / "report.md").write_text(summary(results), encoding="utf-8")
                    print(json.dumps({k: row[k] for k in ("engine", "piece_bytes", "condition", "trial", "ok")}), flush=True)
    if any(not row["ok"] for row in results):
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
