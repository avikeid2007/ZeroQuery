"use client";

import { useState } from "react";
import ConnectionForm from "@/components/ConnectionForm";
import TablePicker from "@/components/TablePicker";
import ConfigPreview from "@/components/ConfigPreview";
import LlmSettingsPanel from "@/components/LlmSettingsPanel";
import { introspectDatabase, ApiError } from "@/lib/api";
import type { DatabaseProvider, IntrospectResponse } from "@/lib/types";
import type { PickerSelection } from "@/lib/selection";

type WizardStep = "connect" | "pick" | "generate";

export default function Home() {
  const [step, setStep] = useState<WizardStep>("connect");
  const [provider, setProvider] = useState<DatabaseProvider>("SqlServer");
  const [connectionString, setConnectionString] = useState<string>("");
  const [schema, setSchema] = useState<IntrospectResponse | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [confirmedSelection, setConfirmedSelection] = useState<PickerSelection | null>(null);

  async function handleConnect(selectedProvider: DatabaseProvider, submittedConnectionString: string) {
    setIsLoading(true);
    setErrorMessage(null);
    try {
      const result = await introspectDatabase({ provider: selectedProvider, connectionString: submittedConnectionString });
      setProvider(selectedProvider);
      setConnectionString(submittedConnectionString);
      setSchema(result);
      setStep("pick");
    } catch (err) {
      setErrorMessage(err instanceof ApiError ? err.message : "Unexpected error reading schema.");
    } finally {
      setIsLoading(false);
    }
  }

  function handleConfirm(selection: PickerSelection) {
    setConfirmedSelection(selection);
    setStep("generate");
  }

  function handleStartOver() {
    setStep("connect");
    setSchema(null);
    setConfirmedSelection(null);
  }

  const steps: { key: WizardStep; label: string }[] = [
    { key: "connect", label: "Connect" },
    { key: "pick", label: "Select" },
    { key: "generate", label: "Launch" },
  ];
  const stepIndex = steps.findIndex((s) => s.key === step);

  return (
    <div className="flex flex-1 flex-col items-center bg-bg px-4 py-8 font-sans sm:px-6">
      <div className="flex w-full max-w-3xl flex-1 flex-col gap-5">
        <div className="flex items-baseline gap-2.5">
          <span className="text-lg font-semibold tracking-tight text-text">Zeroquery</span>
          <span className="text-sm text-muted">point at a database, get an app</span>
        </div>

        <LlmSettingsPanel />

        <div className="overflow-hidden rounded-lg border border-border bg-surface">
          <div className="flex items-center justify-between border-b border-border px-4 py-3">
            <div className="flex items-center gap-2.5 text-sm">
              <span className="h-1.5 w-1.5 rounded-full bg-teal" />
              <span className="text-muted">
                {schema ? `${schema.tables.length} tables discovered` : "Not connected"}
              </span>
            </div>
          </div>

          <div className="grid grid-cols-[136px_minmax(0,1fr)] sm:grid-cols-[136px_minmax(0,1fr)]">
            <div className="shrink-0 border-r border-border py-4">
              {steps.map((s, i) => (
                <div
                  key={s.key}
                  className={`border-l-2 px-4 py-2.5 text-sm ${
                    i === stepIndex
                      ? "border-teal bg-surface2 text-text"
                      : "border-transparent text-muted"
                  }`}
                >
                  <span className={`mr-2 ${i === stepIndex ? "text-teal" : "text-muted"}`}>{i + 1}</span>
                  {s.label}
                </div>
              ))}
            </div>

            <div className="min-w-0 overflow-x-auto p-5">
              {step === "connect" && (
                <ConnectionForm onSubmit={handleConnect} isLoading={isLoading} errorMessage={errorMessage} />
              )}

              {step === "pick" && schema && (
                <TablePicker schema={schema} onBack={() => setStep("connect")} onConfirm={handleConfirm} />
              )}

              {step === "generate" && schema && confirmedSelection && (
                <div className="flex w-full flex-col gap-4">
                  <ConfigPreview
                    provider={provider}
                    connectionString={connectionString}
                    schema={schema}
                    selection={confirmedSelection}
                    onBack={() => setStep("pick")}
                  />
                  <button
                    type="button"
                    onClick={handleStartOver}
                    className="mx-auto rounded-md px-4 py-2 text-sm font-medium text-muted hover:bg-surface2"
                  >
                    Start over with a different database
                  </button>
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
