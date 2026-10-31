"""Isolated benchmark adapter; not imported or shipped by AniT."""
import argparse
import json
import time
from pathlib import Path


def run(root, destination, port, warm):
    import libtorrent as lt
    ti = lt.torrent_info(str(root / "fixture.torrent"))
    fs = ti.orig_files()
    ordered = sorted(range(fs.num_files()), key=fs.file_path)
    selected = ordered[1]
    if warm:
        target = destination / fs.file_path(selected)
        target.parent.mkdir(parents=True, exist_ok=True)
        import shutil
        shutil.copyfile(root / "Original" / "Authorized - 02.bin", target)
    # Loopback only. No DHT, tracker, LAN discovery, UPnP or NAT-PMP traffic.
    session = lt.session({
        "listen_interfaces": "127.0.0.1:0", "outgoing_interfaces": "127.0.0.1",
        "enable_dht": False, "enable_lsd": False, "enable_upnp": False,
        "enable_natpmp": False, "enable_incoming_utp": False, "enable_outgoing_utp": False,
        "ignore_limits_on_local_network": False, "download_rate_limit": 0,
        "alert_mask": int(lt.alert.category_t.all_categories),
    })
    params = lt.add_torrent_params()
    params.ti = ti
    params.save_path = str(destination)
    params.file_priorities = [4 if i == selected else 0 for i in range(fs.num_files())]
    params.flags &= ~(lt.torrent_flags.paused | lt.torrent_flags.auto_managed)
    started = time.perf_counter()
    elapsed = lambda: (time.perf_counter() - started) * 1000
    handle = session.add_torrent(params)
    peer_ms = data_ms = None
    connected = False
    reads = []
    buffers = {}
    def tick():
        nonlocal peer_ms, data_ms, connected
        if elapsed() > 60000:
            raise TimeoutError("Trial exceeded 60 seconds: " + str(handle.status()) + " reads=" + repr(reads))
        for alert in session.pop_alerts():
            if isinstance(alert, lt.read_piece_alert):
                if alert.error.value() != 0:
                    raise IOError(alert.error.message())
                buffers[alert.piece] = bytes(alert.buffer)
            elif isinstance(alert, lt.torrent_error_alert):
                raise IOError(alert.error.message())
        status = handle.status()
        if status.num_peers and peer_ms is None:
            peer_ms = elapsed()
        if status.total_payload_download and data_ms is None:
            data_ms = elapsed()
        if not connected and status.state not in (lt.torrent_status.checking_files, lt.torrent_status.checking_resume_data):
            handle.connect_peer(("127.0.0.1", port))
            connected = True
        return status
    last_debug = 0
    try:
        while tick().state in (lt.torrent_status.checking_files, lt.torrent_status.checking_resume_data):
            time.sleep(0.01)
        size, start = fs.file_size(selected), fs.file_offset(selected)
        for offset in (0, size * 3 // 4, size // 4, 0):
            before = elapsed()
            absolute = start + offset
            first = absolute // ti.piece_length()
            last = (absolute + 65536 - 1) // ti.piece_length()
            handle.clear_piece_deadlines()
            # Prioritize the requested range and two pieces of forward reserve.
            for piece in range(first, min(ti.num_pieces(), last + 3)):
                if piece * ti.piece_length() >= start + size:
                    break
                handle.set_piece_deadline(piece, (piece - first) * 500)
            pending = set(range(first, last + 1))
            requested = set()
            while pending:
                status = tick()
                if elapsed() - last_debug > 5000:
                    import sys
                    print(json.dumps({"ms": elapsed(), "offset": offset, "wanted": list(pending), "have": [handle.have_piece(p) for p in pending], "peers": status.num_peers, "bytes": status.total_payload_download, "state": str(status.state), "download_rate": status.download_payload_rate, "error": status.errc.message(), "requested_priorities": {p: handle.piece_priority(p) for p in pending}, "queue": len(handle.get_download_queue())}), file=sys.stderr, flush=True)
                    last_debug = elapsed()
                for piece in list(pending):
                    if piece in buffers:
                        pending.remove(piece)
                    elif piece not in requested and handle.have_piece(piece):
                        handle.read_piece(piece)
                        requested.add(piece)
                if pending:
                    time.sleep(0.01)
            data = b"".join(buffers[piece] for piece in range(first, last + 1))
            delta = absolute - first * ti.piece_length()
            data = data[delta:delta + 65536]
            with (root / "Original" / "Authorized - 02.bin").open("rb") as original:
                original.seek(offset)
                if data != original.read(65536):
                    raise IOError("Verified range differs from source")
            reads.append({"offset": offset, "ready_ms": elapsed(), "wait_ms": elapsed() - before,
                          "bytes": len(data), "verified": True})
            # Do not reuse process-local read buffers for the return-to-start test.
            buffers.clear()
        status = tick()
        other_pieces = set()
        for index in ordered:
            if index == selected:
                continue
            first = fs.file_offset(index) // ti.piece_length()
            last = (fs.file_offset(index) + fs.file_size(index) - 1) // ti.piece_length()
            other_pieces.update(range(first, last + 1))
        return {"engine": "libtorrent", "version": lt.version, "warm": warm,
                "piece_bytes": ti.piece_length(), "peer_ms": peer_ms, "first_data_ms": data_ms,
                "reads": reads, "received_bytes": status.total_payload_download,
                "selected_percent": handle.file_progress()[selected] * 100 / size,
                "unselected_pieces": sum(handle.have_piece(i) for i in other_pieces), "total_ms": elapsed()}
    finally:
        handle.pause()
        session.remove_torrent(handle)
        session.pause()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    parser.add_argument("destination", type=Path)
    parser.add_argument("port", type=int)
    parser.add_argument("condition", choices=("cold", "warm"))
    args = parser.parse_args()
    print(json.dumps(run(args.root, args.destination, args.port, args.condition == "warm")))
