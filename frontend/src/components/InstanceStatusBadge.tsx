import type { DabInstanceStatusName } from "@/lib/types";
import StatusPill, { type StatusTone } from "@/components/StatusPill";

const STATUS_TONES: Record<DabInstanceStatusName, StatusTone> = {
  Provisioning: "starting",
  Starting: "starting",
  Running: "running",
  Idle: "idle",
  Stopped: "idle",
  Error: "error",
};

const STATUS_LABELS: Record<DabInstanceStatusName, string> = {
  Provisioning: "provisioning",
  Starting: "starting",
  Running: "running",
  Idle: "idle",
  Stopped: "stopped",
  Error: "error",
};

export default function InstanceStatusBadge({ status }: { status: DabInstanceStatusName }) {
  return <StatusPill tone={STATUS_TONES[status]} label={STATUS_LABELS[status]} />;
}
