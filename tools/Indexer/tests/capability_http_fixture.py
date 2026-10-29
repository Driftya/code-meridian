"""Bounded synthetic ingestion transport. This fixture does not emulate Neo4j or queries."""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import threading


class CapabilityHttpFixture:
    def __init__(self):
        self.generation = 0
        self.snapshot = None
        self.outcomes = []
        self.requests = []
        self.errors = []
        self.fail_documents = False
        self.fail_nodes = False
        self.fail_edges = False
        self.on_document = None
        fixture = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_):
                pass

            def reply(self, value, status=200):
                payload = json.dumps(value).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(payload)))
                self.end_headers()
                self.wfile.write(payload)

            def body(self):
                if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
                    parts = []
                    while True:
                        length = int(self.rfile.readline().split(b";", 1)[0], 16)
                        if length == 0:
                            while self.rfile.readline() not in (b"\r\n", b"\n", b""):
                                pass
                            break
                        parts.append(self.rfile.read(length))
                        assert self.rfile.read(2) == b"\r\n"
                    return json.loads(b"".join(parts))
                return json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"null")

            def do_GET(self):
                self.handle_request("GET")

            def do_POST(self):
                self.handle_request("POST")

            def do_DELETE(self):
                self.handle_request("DELETE")

            def handle_request(self, method):
                try:
                    assert self.headers.get("Authorization") == "Bearer matrix-key", "Missing authentication"
                    path = self.path
                    data = self.body() if method == "POST" else None
                    fixture.requests.append((method, path, data))
                    if method == "GET" and path == "/api/v1/embeddings/availability":
                        self.reply({}, 503)
                    elif method == "GET" and path.startswith("/api/v1/project-profiles/"):
                        self.reply({"contractVersion": "1.0", "status": "available", "project": "Matrix",
                            "generation": fixture.generation, "latestGeneration": fixture.generation,
                            "profile": fixture.snapshot["profile"] if fixture.snapshot else None, "warnings": []})
                    elif method == "POST" and path.endswith("/project-profiles/begin"):
                        fixture.generation += 1
                        self.reply({"generation": fixture.generation})
                    elif method == "POST" and path == "/api/v1/project-profiles/":
                        fixture.snapshot = data
                        self.reply({"generation": fixture.generation})
                    elif method == "POST" and path.endswith("/outcomes"):
                        fixture.outcomes.extend(data["outcomes"])
                        self.reply({})
                    elif method == "POST" and path.endswith("/powershell"):
                        assert data["generation"] == fixture.generation
                        assert data["sourceFingerprint"] == fixture.snapshot["profile"]["sourceFingerprint"]
                        self.reply({})
                    elif method == "POST" and path == "/api/v1/knowledge/sql":
                        assert data["generation"] == fixture.generation
                        assert data["sourceFingerprint"] == fixture.snapshot["profile"]["sourceFingerprint"]
                        assert data["fingerprint"] == next(item for item in fixture.snapshot["profile"]["analyzers"] if item["id"] == "sql")["fingerprint"]
                        self.reply({})
                    elif method == "POST" and path in ("/api/v1/knowledge/nodes", "/api/v1/knowledge/nodes/bulk") and fixture.fail_nodes:
                        self.reply({}, 503)
                    elif method == "POST" and path in ("/api/v1/knowledge/nodes/edges", "/api/v1/knowledge/nodes/edges/bulk") and fixture.fail_edges:
                        self.reply({}, 503)
                    elif method == "POST" and path in (
                        "/api/v1/knowledge/nodes", "/api/v1/knowledge/nodes/bulk",
                        "/api/v1/knowledge/nodes/edges", "/api/v1/knowledge/nodes/edges/bulk",
                        "/api/v1/knowledge/sql", "/api/v1/knowledge/package-references/begin",
                        "/api/v1/knowledge/package-references", "/api/v1/knowledge/package-references/reconcile"):
                        self.reply({})
                    elif method == "POST" and path in (
                        "/api/v1/knowledge/documents", "/api/v1/knowledge/documents/bulk"):
                        if fixture.on_document:
                            action, fixture.on_document = fixture.on_document, None
                            action()
                        self.reply({}, 503 if fixture.fail_documents else 200)
                    elif method == "DELETE" and path.startswith("/api/v1/knowledge/project/Matrix/files/"):
                        self.reply({})
                    else:
                        raise AssertionError("Unexpected request: " + method + " " + path)
                except Exception as error:
                    fixture.errors.append(str(error))
                    self.reply({"error": str(error)}, 500)

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)

    def __enter__(self):
        self.thread.start()
        return self

    def __exit__(self, *_):
        self.server.shutdown()
        self.server.server_close()

    @property
    def url(self):
        return f"http://127.0.0.1:{self.server.server_port}"
