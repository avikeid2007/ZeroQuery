/**
 * Small rounded status pill with a colored/animated dot, matching the `.status` component
 * from doc/Zeroquery — Mock Design.html (starting/running/idle/error variants).
 */
export type StatusTone = "starting" | "running" | "idle" | "error";

const TONE_STYLES: Record<StatusTone, string> = {
  running: "text-teal border-teal/40",
  starting: "text-amber border-amber/40",
  idle: "text-muted border-border",
  error: "text-danger border-danger/40",
};

const DOT_STYLES: Record<StatusTone, string> = {
  running: "bg-teal",
  starting: "bg-amber animate-[zq-pulse_1.1s_ease-in-out_infinite]",
  idle: "bg-muted",
  error: "bg-danger",
};

export default function StatusPill({ tone, label }: { tone: StatusTone; label: string }) {
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 pl-2 text-xs ${TONE_STYLES[tone]}`}
    >
      <span className={`h-1.5 w-1.5 rounded-full ${DOT_STYLES[tone]}`} />
      {label}
    </span>
  );
}
