"""Validation of benchmark reporting, without importing libtorrent or network access."""
import importlib.util
import math
import io
from types import SimpleNamespace
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("compare", Path(__file__).resolve().parents[1] / "tools/TorrentEngineBench/compare.py")
compare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compare)


def valid():
    return {"engine": "MonoTorrent", "piece_bytes": 65536, "condition": "cold", "trial": 0,
            "warm": False, "ok": True, "unselected_pieces": 0, "received_bytes": 500000,
            "reads": [{"offset": offset, "ready_ms": (index + 1) * 10, "wait_ms": 10,
                       "bytes": 65536, "verified": True}
                      for index, offset in enumerate((0, 12 * 1024 * 1024, 4 * 1024 * 1024, 0))]}


class BenchTests(unittest.TestCase):
    def test_old_seed_binary_without_external_limiter_is_rejected(self):
        process = SimpleNamespace(stdout=io.StringIO('{"ready":true,"port":1234}\n'), stderr=io.StringIO())
        with self.assertRaises(RuntimeError):
            compare.read_ready(process)
        process = SimpleNamespace(stdout=io.StringIO('{"ready":true,"port":1234,"wire_bytes_per_second":1048576}\n'), stderr=io.StringIO())
        self.assertEqual(1234, compare.read_ready(process)["port"])

    def test_invalid_ranges_or_partial_trials_cannot_pass(self):
        compare.validate_result(valid())
        for change in (lambda row: row["reads"].pop(),
                       lambda row: row["reads"][0].update(verified=False),
                       lambda row: row["reads"][1].update(offset=0),
                       lambda row: row["reads"][0].update(ready_ms=math.nan),
                       lambda row: row.update(unselected_pieces=1),
                       lambda row: row.update(condition="warm", warm=True)):
            row = valid(); change(row)
            with self.assertRaises(ValueError):
                compare.validate_result(row)

    def test_failures_are_counted_but_never_improve_the_median(self):
        good = valid()
        failed = {**valid(), "ok": False, "error": "timeout", "trial": 1}
        failed["reads"] = []
        report = compare.summary([good, failed])
        self.assertIn("1/2", report)
        self.assertIn("10.0 / 10.0", report)
        self.assertIn("timeout", report)
        self.assertIn("0/1", compare.summary([failed]))
        self.assertIn("n/d", compare.summary([failed]))

    def test_warm_results_require_zero_network_content(self):
        row = valid(); row.update(condition="warm", warm=True, received_bytes=0)
        compare.validate_result(row)
        row["received_bytes"] = 1
        with self.assertRaises(ValueError):
            compare.validate_result(row)


if __name__ == "__main__":
    unittest.main()
