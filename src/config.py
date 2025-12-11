# -*- coding: utf-8 -*-
"""Application configuration management."""
import os
from pathlib import Path
from dotenv import load_dotenv

# Load environment variables
load_dotenv()

# Base paths
BASE_DIR = Path(__file__).resolve().parent.parent
DATA_DIR = BASE_DIR / "data"
DATA_DIR.mkdir(exist_ok=True)


class Settings:
    """Application settings."""

    # Server
    HOST: str = os.getenv("HOST", "0.0.0.0")
    PORT: int = int(os.getenv("PORT", "8000"))
    DEBUG: bool = os.getenv("DEBUG", "false").lower() == "true"

    # Database
    DATABASE_PATH: Path = DATA_DIR / "market.db"
    DATABASE_URL: str = f"sqlite+aiosqlite:///{DATABASE_PATH}"

    # Crawler
    CRAWL_INTERVAL_MINUTES: int = int(os.getenv("CRAWL_INTERVAL_MINUTES", "5"))
    REQUEST_DELAY_SECONDS: float = float(os.getenv("REQUEST_DELAY_SECONDS", "1.0"))

    # GNJOY API
    GNJOY_BASE_URL: str = os.getenv("GNJOY_BASE_URL", "https://ro.gnjoy.com/itemDeal")

    # Endpoints
    TOP5_ENDPOINT: str = f"{GNJOY_BASE_URL}/itemTop5BestView.asp"
    DEAL_LIST_ENDPOINT: str = f"{GNJOY_BASE_URL}/itemDealList.asp"

    # Server IDs (GNJOY uses different IDs internally)
    # API request uses -1, 1, 2, 3, 4
    # GNJOY response uses 129, 229, 529, 729
    SERVERS: dict = {
        -1: "전체",
        1: "바포메트",
        2: "이그드라실",
        3: "다크로드",
        4: "이프리트",
        # GNJOY internal IDs
        129: "바포메트",
        229: "이그드라실",
        529: "다크로드",
        729: "이프리트",
    }

    # Mapping from GNJOY internal ID to API ID
    GNJOY_SERVER_MAP: dict = {
        129: 1,
        229: 2,
        529: 3,
        729: 4,
    }


settings = Settings()
