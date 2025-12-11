# -*- coding: utf-8 -*-
"""Scheduler for periodic crawling jobs."""
import asyncio
import logging
from datetime import datetime
from apscheduler.schedulers.asyncio import AsyncIOScheduler
from apscheduler.triggers.interval import IntervalTrigger

from src.config import settings
from src.crawler import GnjoyClient
from src.database import ItemRepository

logger = logging.getLogger(__name__)


class CrawlerScheduler:
    """Scheduler for periodic market data crawling."""

    def __init__(self, client: GnjoyClient, repo: ItemRepository):
        self.client = client
        self.repo = repo
        self.scheduler = AsyncIOScheduler()
        self._is_running = False

    def start(self):
        """Start the scheduler with configured jobs."""
        if self._is_running:
            logger.warning("Scheduler already running")
            return

        # Add top5 crawl job
        self.scheduler.add_job(
            self._crawl_top5,
            trigger=IntervalTrigger(minutes=settings.CRAWL_INTERVAL_MINUTES),
            id="crawl_top5",
            name="Crawl Top 5 Items",
            replace_existing=True,
        )

        self.scheduler.start()
        self._is_running = True
        logger.info(f"Scheduler started (interval: {settings.CRAWL_INTERVAL_MINUTES}min)")

        # Run initial crawl
        asyncio.create_task(self._initial_crawl())

    def stop(self):
        """Stop the scheduler."""
        if self._is_running:
            self.scheduler.shutdown(wait=False)
            self._is_running = False
            logger.info("Scheduler stopped")

    async def _initial_crawl(self):
        """Run initial data crawl on startup."""
        logger.info("Running initial data crawl...")
        await self._crawl_top5()

    async def _crawl_top5(self):
        """Crawl top 5 items and save to database."""
        try:
            logger.info(f"Starting top5 crawl at {datetime.now().isoformat()}")

            result = await self.client.fetch_top5_items()

            if result is None:
                logger.error("Failed to fetch top5 items")
                return

            # Save each category
            await self.repo.save_top_items(result.weapons, "W")
            await self.repo.save_top_items(result.defenses, "D")
            await self.repo.save_top_items(result.consumables, "C")
            await self.repo.save_top_items(result.etcs, "E")

            total = (
                len(result.weapons) +
                len(result.defenses) +
                len(result.consumables) +
                len(result.etcs)
            )
            logger.info(f"Crawl complete: {total} items saved")

        except Exception as e:
            logger.error(f"Error in top5 crawl: {e}")

    @property
    def is_running(self) -> bool:
        """Check if scheduler is running."""
        return self._is_running

    def get_jobs(self) -> list[dict]:
        """Get list of scheduled jobs."""
        jobs = []
        for job in self.scheduler.get_jobs():
            jobs.append({
                "id": job.id,
                "name": job.name,
                "next_run": job.next_run_time.isoformat() if job.next_run_time else None,
            })
        return jobs
