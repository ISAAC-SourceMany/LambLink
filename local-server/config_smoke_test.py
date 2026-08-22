#!/usr/bin/env python3
from __future__ import annotations

import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
AWS_BACKEND_DIR = ROOT / "aws" / "backend"
sys.path.insert(0, str(AWS_BACKEND_DIR))

from configuration import load_chzzk_configuration, ConfigurationError


def masked(value: str) -> str:
    if len(value) <= 8:
        return "*" * len(value)
    return value[:4] + "..." + value[-4:]


try:
    cfg = load_chzzk_configuration()
    print("[CONFIG] provider:", cfg.source)
    print("[CONFIG] companion client-id:", masked(cfg.companion.client_id))
    print("[CONFIG] companion client-secret: loaded (length=%d)" % len(cfg.companion.client_secret))
    print("[CONFIG] mylamb client-id:", masked(cfg.mylamb.client_id))
    print("[CONFIG] mylamb client-secret: loaded (length=%d)" % len(cfg.mylamb.client_secret))
    print("[CONFIG] OK")
except Exception as exc:
    print("[CONFIG] FAILED:", exc)
    raise
