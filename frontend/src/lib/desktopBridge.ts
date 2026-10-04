import type { OrchestrationProgressDto, UiSpecResponse } from "./types";

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (message: unknown) => void;
        addEventListener: (event: "message", handler: (event: { data: unknown }) => void) => void;
        removeEventListener: (event: "message", handler: (event: { data: unknown }) => void) => void;
      };
    };
  }
}

interface IpcMessage {
  id: string;
  channel: string;
  success?: boolean;
  data?: unknown;
  error?: string;
  event?: "progress" | "result" | "error";
}

export function isDesktopApp(): boolean {
  return typeof window !== "undefined" && Boolean(window.chrome?.webview);
}

class DesktopBridge {
  private pendingRequests = new Map<string, {
    resolve: (data: any) => void;
    reject: (error: Error) => void;
  }>();

  private streamingRequests = new Map<string, {
    onProgress: (progress: OrchestrationProgressDto) => void;
    resolve: (result: UiSpecResponse) => void;
    reject: (error: Error) => void;
  }>();

  private initialized = false;

  constructor() {
    this.init();
  }

  private init() {
    if (typeof window === "undefined" || !window.chrome?.webview || this.initialized) {
      return;
    }

    window.chrome.webview.addEventListener("message", (event) => {
      this.handleIncomingMessage(event.data);
    });

    this.initialized = true;
  }

  public notifyAppReady() {
    this.init();
    if (typeof window !== "undefined" && window.chrome?.webview) {
      try {
        window.chrome.webview.postMessage({ type: "app:ready" });
      } catch {
        // ignore
      }
    }
  }

  private handleIncomingMessage(raw: unknown) {
    let msg: IpcMessage;
    if (typeof raw === "string") {
      try {
        msg = JSON.parse(raw) as IpcMessage;
      } catch {
        return;
      }
    } else if (typeof raw === "object" && raw !== null) {
      msg = raw as IpcMessage;
    } else {
      return;
    }

    const { id, channel, event } = msg;

    // Handle streaming query messages
    if (channel === "query/stream" && this.streamingRequests.has(id)) {
      const stream = this.streamingRequests.get(id)!;
      if (event === "progress") {
        stream.onProgress(msg.data as OrchestrationProgressDto);
      } else if (event === "result") {
        this.streamingRequests.delete(id);
        stream.resolve(msg.data as UiSpecResponse);
      } else if (event === "error") {
        this.streamingRequests.delete(id);
        stream.reject(new Error(msg.error || "Desktop streaming query failed."));
      }
      return;
    }

    // Handle unary request/response messages
    if (this.pendingRequests.has(id)) {
      const { resolve, reject } = this.pendingRequests.get(id)!;
      this.pendingRequests.delete(id);

      if (msg.success) {
        resolve(msg.data);
      } else {
        reject(new Error(msg.error || `IPC call to '${channel}' failed.`));
      }
    }
  }

  public invoke<TRequest = unknown, TResponse = unknown>(
    channel: string,
    payload?: TRequest,
  ): Promise<TResponse> {
    this.init();
    if (!window.chrome?.webview) {
      return Promise.reject(new Error("Native WebView2 IPC is not available outside the Desktop app."));
    }

    const id = `${Date.now()}-${Math.random().toString(36).slice(2, 9)}`;

    return new Promise<TResponse>((resolve, reject) => {
      this.pendingRequests.set(id, { resolve, reject });
      window.chrome!.webview!.postMessage({ id, channel, payload });
    });
  }

  public stream(
    channel: string,
    payload: unknown,
    onProgress: (progress: OrchestrationProgressDto) => void,
    signal?: AbortSignal,
  ): Promise<UiSpecResponse> {
    this.init();
    if (!window.chrome?.webview) {
      return Promise.reject(new Error("Native WebView2 IPC is not available outside the Desktop app."));
    }

    const id = `${Date.now()}-${Math.random().toString(36).slice(2, 9)}`;

    return new Promise<UiSpecResponse>((resolve, reject) => {
      if (signal?.aborted) {
        return reject(new Error("Query aborted."));
      }

      const cleanup = () => {
        this.streamingRequests.delete(id);
      };

      if (signal) {
        signal.addEventListener("abort", () => {
          cleanup();
          // Notify C# to abort if supported
          window.chrome?.webview?.postMessage({ id, channel: "query/abort" });
          reject(new Error("Query aborted by user."));
        });
      }

      this.streamingRequests.set(id, {
        onProgress,
        resolve: (data) => {
          cleanup();
          resolve(data);
        },
        reject: (err) => {
          cleanup();
          reject(err);
        },
      });

      window.chrome!.webview!.postMessage({ id, channel, payload });
    });
  }
}

export const desktopBridge = new DesktopBridge();
