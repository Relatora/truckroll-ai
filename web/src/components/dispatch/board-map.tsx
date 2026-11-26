"use client";

import "maplibre-gl/dist/maplibre-gl.css";
import * as maplibregl from "maplibre-gl";
import { useEffect, useRef } from "react";
import { equipmentTone } from "@/lib/customers";
import type { Board } from "@/lib/dispatch";

// The worker module is copied to /maplibre/ by scripts/copy-maplibre-worker.mjs (bundlers don't emit it).
maplibregl.setWorkerUrl("/maplibre/maplibre-gl-worker.mjs");

const TONE_COLOURS = { ember: "var(--ember)", frost: "var(--frost)", aqua: "var(--aqua)", violet: "var(--violet)" } as const;

/**
 * The branch's day on a map: every job as a pin in its trade colour (waiting jobs hollow,
 * emergencies pulsing), and each technician's truck where they are now: their job on site, their
 * last finished job, or the shop.
 *
 * Tiles come from OpenFreeMap (free, no key; attribution shown). In dark mode the light basemap is
 * inverted on the canvas only, so the pins keep their true colours.
 */
export function BoardMap({ board, selected, onSelect }: { board: Board; selected: number | null; onSelect: (jobId: number) => void }) {
  const container = useRef<HTMLDivElement>(null);
  const map = useRef<maplibregl.Map | null>(null);
  const markers = useRef<maplibregl.Marker[]>([]);
  const select = useRef(onSelect);
  useEffect(() => {
    select.current = onSelect;
  });

  useEffect(() => {
    if (!container.current) return;
    const instance = new maplibregl.Map({
      container: container.current,
      style: "https://tiles.openfreemap.org/styles/positron",
      center: [board.branch.longitude, board.branch.latitude],
      zoom: 11.2,
      attributionControl: { compact: true },
    });
    instance.addControl(new maplibregl.NavigationControl({ showCompass: false }), "top-right");
    map.current = instance;
    return () => {
      instance.remove();
      map.current = null;
    };
    // The map is created once per branch; jobs update through the markers effect below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [board.branch.id]);

  useEffect(() => {
    const instance = map.current;
    if (!instance) return;
    markers.current.forEach((marker) => marker.remove());
    markers.current = [];

    for (const job of board.jobs) {
      const element = document.createElement("button");
      element.type = "button";
      element.setAttribute("aria-label", `${job.number}: ${job.summary}`);
      const colour = TONE_COLOURS[equipmentTone(job.equipmentKind)];
      const waiting = job.technicianId === null;
      const emergency = job.priority === "Emergency" && job.status !== "Completed";
      element.className = "board-pin";
      element.style.setProperty("--pin", emergency ? "var(--ember)" : colour);
      element.dataset.waiting = String(waiting);
      element.dataset.emergency = String(emergency);
      element.dataset.selected = String(job.id === selected);
      element.dataset.done = String(job.status === "Completed");
      element.addEventListener("click", () => select.current(job.id));
      markers.current.push(new maplibregl.Marker({ element }).setLngLat([job.longitude, job.latitude]).addTo(instance));
    }

    for (const technician of board.technicians) {
      const theirs = board.jobs.filter((job) => job.technicianId === technician.id);
      const now = theirs.find((job) => job.status === "InProgress") ?? [...theirs].reverse().find((job) => job.status === "Completed");
      const element = document.createElement("div");
      element.className = "board-truck";
      element.style.setProperty("--truck", technician.color);
      element.textContent = technician.name.split(" ").map((part) => part[0]).join("");
      element.title = `${technician.name}${now ? `, at ${now.street}` : ", at the shop"}`;
      const at: [number, number] = now ? [now.longitude + 0.0025, now.latitude + 0.0018] : [board.branch.longitude, board.branch.latitude];
      markers.current.push(new maplibregl.Marker({ element }).setLngLat(at).addTo(instance));
    }
  }, [board, selected]);

  // Fly to a job when it's picked in the lanes.
  useEffect(() => {
    const job = board.jobs.find((j) => j.id === selected);
    if (job && map.current) map.current.flyTo({ center: [job.longitude, job.latitude], zoom: 13, speed: 1.4 });
  }, [selected, board.jobs]);

  return <div ref={container} className="board-map h-full w-full" />;
}
