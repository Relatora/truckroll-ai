"use client";

import { useEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from "react";
import { monthLabel, type MonthPoint } from "@/lib/dashboard";

/**
 * Two small hand-drawn SVG charts, built to the dataviz spec: 2px lines, columns no wider than
 * 24px with a 4px rounded data end, hairline grid, one y-axis, a hover/keyboard layer, and a
 * legend whenever there's more than one series. Values in tooltips are also in the table view.
 */

const HEIGHT = 240;
const MARGIN = { top: 14, right: 18, bottom: 28, left: 56 };

type Series = { code: string; name: string; colour: string };

function useWidth() {
  const ref = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(640);
  useEffect(() => {
    const node = ref.current;
    if (!node) return;
    const observer = new ResizeObserver(([entry]) => setWidth(Math.max(280, entry.contentRect.width)));
    observer.observe(node);
    return () => observer.disconnect();
  }, []);
  return { ref, width };
}

/** A clean top for the axis and four evenly spaced ticks. */
function niceTicks(max: number) {
  if (max <= 0) return { top: 1, ticks: [0, 1] };
  const rough = max / 4;
  const power = 10 ** Math.floor(Math.log10(rough));
  const step = [1, 2, 2.5, 5, 10].map((m) => m * power).find((s) => s >= rough)!;
  const top = Math.ceil(max / step) * step;
  return { top, ticks: Array.from({ length: Math.round(top / step) + 1 }, (_, i) => i * step) };
}

function Axes({ width, ticks, top, format, months, every }: { width: number; ticks: number[]; top: number; format: (v: number) => string; months: MonthPoint[]; every: number }) {
  const plotW = width - MARGIN.left - MARGIN.right;
  const plotH = HEIGHT - MARGIN.top - MARGIN.bottom;
  return (
    <g fontSize={11} fill="var(--muted-foreground)" className="tabular">
      {ticks.map((t) => {
        const y = MARGIN.top + plotH * (1 - t / top);
        return (
          <g key={t}>
            <line x1={MARGIN.left} x2={width - MARGIN.right} y1={y} y2={y} stroke={t === 0 ? "var(--chart-axis)" : "var(--chart-grid)"} strokeWidth={1} />
            <text x={MARGIN.left - 8} y={y} dy="0.32em" textAnchor="end">
              {format(t)}
            </text>
          </g>
        );
      })}
      {months.map((m, i) =>
        i % every === 0 || i === months.length - 1 ? (
          <text key={m.month} x={MARGIN.left + (plotW * (i + 0.5)) / months.length} y={HEIGHT - 8} textAnchor="middle">
            {monthLabel(m.month)}
          </text>
        ) : null,
      )}
    </g>
  );
}

function Tooltip({ x, width, title, rows }: { x: number; width: number; title: string; rows: { key: string; colour: string; label: string; value: string }[] }) {
  // Keep it inside the chart: flip to the left of the pointer near the right edge.
  const left = x > width - 200 ? x - 186 : x + 14;
  return (
    <div role="status" className="glass-strong pointer-events-none absolute top-2 z-10 w-44 rounded-xl bg-card/95 p-2.5 text-xs shadow-lift" style={{ left }}>
      <p className="mb-1.5 font-medium text-muted-foreground">{title}</p>
      <ul className="space-y-1">
        {rows.map((r) => (
          <li key={r.key} className="flex items-center gap-2">
            <span className="h-0.5 w-3 shrink-0 rounded-full" style={{ background: r.colour }} />
            <span className="font-semibold text-foreground tabular">{r.value}</span>
            <span className="truncate text-muted-foreground">{r.label}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

/** Revenue by month, one 2px line per branch, with a crosshair that reads every branch at once. */
export function LineChart({
  months,
  series,
  value,
  format,
  axisFormat = format,
  label,
}: {
  months: MonthPoint[];
  series: Series[];
  value: (m: MonthPoint, code: string) => number;
  format: (v: number) => string;
  /** Tick labels, given the axis top: one style per axis, so all compact once it's wide. */
  axisFormat?: (v: number, top: number) => string;
  label: string;
}) {
  const { ref, width } = useWidth();
  const [hover, setHover] = useState<number | null>(null);
  const plotW = width - MARGIN.left - MARGIN.right;
  const plotH = HEIGHT - MARGIN.top - MARGIN.bottom;
  const { top, ticks } = niceTicks(Math.max(0, ...months.flatMap((m) => series.map((s) => value(m, s.code)))));
  const x = (i: number) => MARGIN.left + (plotW * (i + 0.5)) / months.length;
  const y = (v: number) => MARGIN.top + plotH * (1 - v / top);

  function pick(event: PointerEvent<SVGRectElement>) {
    const box = event.currentTarget.getBoundingClientRect();
    const fraction = (event.clientX - box.left) / box.width;
    setHover(Math.min(months.length - 1, Math.max(0, Math.floor(fraction * months.length))));
  }

  function step(event: KeyboardEvent<SVGSVGElement>) {
    if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
    event.preventDefault();
    setHover((h) => Math.min(months.length - 1, Math.max(0, (h ?? months.length - 1) + (event.key === "ArrowRight" ? 1 : -1))));
  }

  return (
    <div ref={ref} className="relative">
      <svg
        width={width}
        height={HEIGHT}
        role="img"
        aria-label={`${label}. Use the left and right arrow keys to read each month.`}
        tabIndex={0}
        onKeyDown={step}
        onBlur={() => setHover(null)}
        className="overflow-visible rounded-lg outline-none focus-visible:ring-2 focus-visible:ring-ring/50"
      >
        <Axes width={width} ticks={ticks} top={top} format={(v) => axisFormat(v, top)} months={months} every={width < 520 ? 3 : 2} />
        {series.map((s) => (
          <path
            key={s.code}
            d={months.map((m, i) => `${i === 0 ? "M" : "L"}${x(i)},${y(value(m, s.code))}`).join(" ")}
            fill="none"
            stroke={s.colour}
            strokeWidth={2}
            strokeLinejoin="round"
            strokeLinecap="round"
          />
        ))}
        {hover !== null && (
          <g>
            <line x1={x(hover)} x2={x(hover)} y1={MARGIN.top} y2={MARGIN.top + plotH} stroke="var(--chart-axis)" strokeWidth={1} />
            {series.map((s) => (
              <circle key={s.code} cx={x(hover)} cy={y(value(months[hover], s.code))} r={4} fill={s.colour} stroke="var(--background)" strokeWidth={2} />
            ))}
          </g>
        )}
        <rect x={MARGIN.left} y={MARGIN.top} width={plotW} height={plotH} fill="transparent" onPointerMove={pick} onPointerLeave={() => setHover(null)} />
      </svg>
      {hover !== null && (
        <Tooltip
          x={x(hover)}
          width={width}
          title={monthLabel(months[hover].month, true)}
          rows={series.map((s) => ({ key: s.code, colour: s.colour, label: s.name, value: format(value(months[hover], s.code)) }))}
        />
      )}
    </div>
  );
}

/** Jobs completed by month: one series of columns, each its own hover and focus target. */
export function ColumnChart({ months, colour, value, label }: { months: MonthPoint[]; colour: string; value: (m: MonthPoint) => number; label: string }) {
  const { ref, width } = useWidth();
  const [hover, setHover] = useState<number | null>(null);
  const plotW = width - MARGIN.left - MARGIN.right;
  const plotH = HEIGHT - MARGIN.top - MARGIN.bottom;
  const band = plotW / months.length;
  const barW = Math.min(24, band * 0.6);
  const { top, ticks } = niceTicks(Math.max(0, ...months.map(value)));
  const format = (v: number) => v.toLocaleString("en-CA");

  return (
    <div ref={ref} className="relative">
      <svg width={width} height={HEIGHT} role="img" aria-label={label} className="overflow-visible">
        <Axes width={width} ticks={ticks} top={top} format={format} months={months} every={width < 520 ? 3 : 2} />
        {months.map((m, i) => {
          const v = value(m);
          const h = (plotH * v) / top;
          const x0 = MARGIN.left + band * i + (band - barW) / 2;
          const base = MARGIN.top + plotH;
          const r = Math.min(4, h);
          // Square at the baseline, 4px rounded at the data end.
          const d = h <= 0 ? "" : `M${x0},${base} V${base - h + r} Q${x0},${base - h} ${x0 + r},${base - h} H${x0 + barW - r} Q${x0 + barW},${base - h} ${x0 + barW},${base - h + r} V${base} Z`;
          return (
            <g
              key={m.month}
              tabIndex={0}
              role="img"
              aria-label={`${monthLabel(m.month, true)}: ${format(v)} jobs`}
              onPointerEnter={() => setHover(i)}
              onPointerLeave={() => setHover(null)}
              onFocus={() => setHover(i)}
              onBlur={() => setHover(null)}
              className="outline-none"
            >
              {/* The hit target is the whole band, not just the painted column. */}
              <rect x={MARGIN.left + band * i} y={MARGIN.top} width={band} height={plotH} fill="transparent" />
              <path d={d} fill={colour} opacity={hover === null || hover === i ? 1 : 0.55} className="transition-opacity" />
            </g>
          );
        })}
      </svg>
      {hover !== null && (
        <Tooltip
          x={MARGIN.left + band * (hover + 0.5)}
          width={width}
          title={monthLabel(months[hover].month, true)}
          rows={[{ key: "jobs", colour, label: "jobs completed", value: format(value(months[hover])) }]}
        />
      )}
    </div>
  );
}

export function Legend({ series }: { series: Series[] }) {
  return (
    <ul className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground" aria-label="Legend">
      {series.map((s) => (
        <li key={s.code} className="flex items-center gap-1.5">
          <span className="h-0.5 w-4 rounded-full" style={{ background: s.colour }} />
          {s.name}
        </li>
      ))}
    </ul>
  );
}
