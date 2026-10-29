import importlib.util
import json
import tempfile
import unittest
import uuid
from pathlib import Path

spec = importlib.util.spec_from_file_location("reporter", Path(__file__).parent.parent / "scripts/Report-OnlinePlayback.py")
reporter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reporter)

class ReportTests(unittest.TestCase):
    def render(self, events, extra=""):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "trace.jsonl"
            path.write_text("\n".join(json.dumps(event) for event in events) + "\n" + extra, encoding="utf-8")
            return reporter.report([path])
    def test_unobserved_frame_does_not_claim_zero_stalls_or_zero_startup(self):
        session = str(uuid.uuid4())
        rows = [dict(SessionId=session, Sequence=1, ElapsedMs=0, Kind="milestone", Name="click", Values={}),
                dict(SessionId=session, Sequence=2, ElapsedMs=200, Kind="summary", Name="cancelled", Values={"rebuffer_count":0,"rebuffer_ms":0})]
        text = self.render(rows)
        self.assertIn("Clique → primeiro sinal de frame | n/d", text)
        self.assertIn("frame: n/d; duração total: n/d", text)
    def test_durations_and_rebuffer_survive_report(self):
        session = str(uuid.uuid4())
        rows = [dict(SessionId=session, Sequence=i+1, ElapsedMs=elapsed, Kind="milestone", Name=name, Values={})
                for i, (name, elapsed) in enumerate([("click",0),("restore_begin",100),("restore_complete",350),("player_requested",500),("first_video_frame_signal",900)])]
        rows.append(dict(SessionId=session, Sequence=6, ElapsedMs=2000, Kind="summary", Name="closed", Values={"rebuffer_count":1,"rebuffer_ms":600}))
        text = self.render(rows)
        self.assertIn("Restauração | 250.0", text)
        self.assertIn("primeiro sinal de frame | 900.0", text)
        self.assertIn("frame: 1; duração total: 600", text)
    def test_incomplete_and_malformed_logs_are_explicit(self):
        rows = [dict(SessionId=str(uuid.uuid4()), Sequence=1, ElapsedMs=0, Kind="milestone", Name="click", Values={})]
        text = self.render(rows, '{bad line\n{}\n[]\n')
        self.assertIn("sem resumo terminal", text)
        self.assertIn("ignoradas: 3", text)

if __name__ == "__main__":
    unittest.main()
