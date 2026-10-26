#!/usr/bin/env python3
import os, subprocess, sys, tempfile, threading
from pathlib import Path
from pyftpdlib.authorizers import DummyAuthorizer
from pyftpdlib.handlers import FTPHandler
from pyftpdlib.servers import FTPServer
with tempfile.TemporaryDirectory(prefix="fluxftp-api-") as directory:
    root=Path(directory); source=root/"source"; target=root/"target"; source.mkdir(); target.mkdir()
    release=source/"releases/Test.Release"; release.mkdir(parents=True)
    (release/"fixture.bin").write_bytes(os.urandom(65543)); (release/"Subs").mkdir(); (release/"Subs/sub.bin").write_bytes(os.urandom(8193))
    (release/"[100%]-COMPLETE").mkdir()
    partial=target/"releases/Test.Release"; partial.mkdir(parents=True); (partial/"fixture.bin").write_bytes((release/"fixture.bin").read_bytes()[:123])
    servers=[]; threads=[]
    # One async loop hosts both FTP listeners; API transfers use real PASV/PORT FXP between them.
    for home in (source,target):
        authorizer=DummyAuthorizer(); authorizer.add_user("test","test",str(home),perm="elradfmwMT")
        handler=type("Fixture",(FTPHandler,),{}); handler.authorizer=authorizer; handler.permit_foreign_addresses=True
        server=FTPServer(("127.0.0.1",0),handler); servers.append(server)
    thread=threading.Thread(target=servers[0].serve_forever,kwargs={"timeout":0.1},daemon=True); thread.start()
    environment=os.environ.copy(); environment["FLUXFTP_API_TEST_LOG"]="1"; environment["FLUXFTP_CONFIG_HOME"]=str(root/"configuration")
    try:
        subprocess.run([sys.argv[1],"--api-check",*[str(server.socket.getsockname()[1]) for server in servers],str(source),str(target)],check=True,env=environment,timeout=110)
    finally:
        for server in servers: server.close_all()
        thread.join(timeout=3)
