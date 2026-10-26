#!/usr/bin/env python3
import os, subprocess, sys, tempfile, threading
from pathlib import Path
from pyftpdlib.authorizers import DummyAuthorizer
from pyftpdlib.handlers import FTPHandler, TLS_FTPHandler
from pyftpdlib.servers import FTPServer
binary = sys.argv[1]
with tempfile.TemporaryDirectory(prefix="fluxftp-ftp-test-") as directory:
    root=Path(directory)
    (root/"fixture.bin").write_bytes(os.urandom(262151))
    subprocess.run(["openssl","req","-x509","-newkey","rsa:2048","-nodes","-keyout",str(root/"key.pem"),"-out",str(root/"cert.pem"),"-days","1","-subj","/CN=localhost"], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    for mode in ("ftp","ftps","openssl"):
        authorizer=DummyAuthorizer(); authorizer.add_user("test","test",directory,perm="elradfmwMT")
        handler=type("Fixture",(FTPHandler if mode=="ftp" else TLS_FTPHandler,),{})
        handler.authorizer=authorizer
        if mode != "ftp":
            handler.certfile=str(root/"cert.pem"); handler.keyfile=str(root/"key.pem"); handler.tls_control_required=True; handler.tls_data_required=True
        server=FTPServer(("127.0.0.1",0),handler)
        port=server.socket.getsockname()[1]
        thread=threading.Thread(target=server.serve_forever,kwargs={"timeout":0.1},daemon=True); thread.start()
        try: subprocess.run([binary,"--ftp-check",str(port),mode,str(root/"fixture.bin")],check=True,timeout=40)
        finally: server.close_all(); thread.join(timeout=3)
