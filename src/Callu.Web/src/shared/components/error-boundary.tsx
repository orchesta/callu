import React from "react";
import { TriangleAlert, RefreshCw, Home, Copy, Check, ArrowLeft } from "lucide-react";
import { Button } from "./ui/button";
import { toast } from "../utils/toast";
import { t } from "../locales/i18n";

interface ErrorBoundaryProps {
  children: React.ReactNode;
}

interface ErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
  errorInfo: React.ErrorInfo | null;
  copied: boolean;
}

interface RouteErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
}

export class RouteErrorBoundary extends React.Component<
  { children: React.ReactNode },
  RouteErrorBoundaryState
> {
  constructor(props: { children: React.ReactNode }) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): Partial<RouteErrorBoundaryState> {
    return { hasError: true, error };
  }

  override componentDidCatch(error: Error, errorInfo: React.ErrorInfo) {
    if (import.meta.env.DEV) {
      console.error("RouteErrorBoundary caught:", error, errorInfo);
    }
  }

  handleRetry = () => {
    this.setState({ hasError: false, error: null });
  };

  handleGoBack = () => {
    this.setState({ hasError: false, error: null });
    window.history.back();
  };

  override render() {
    if (this.state.hasError) {
      return (
        <div className="flex items-center justify-center min-h-[calc(100vh-4rem)] p-6">
          <div className="max-w-md w-full text-center space-y-6">
            <div className="mx-auto w-16 h-16 rounded-2xl bg-error-500/10 border border-error-500/20 flex items-center justify-center">
              <TriangleAlert className="w-8 h-8 text-error-500" />
            </div>
            <div className="space-y-2">
              <h2 className="text-xl font-bold">{t("shared.errorBoundary.routeTitle")}</h2>
              <p className="text-sm text-muted-foreground">{t("shared.errorBoundary.routeDescription")}</p>
            </div>
            {import.meta.env.DEV && this.state.error && (
              <div className="bg-muted/50 rounded-lg p-4 text-left">
                <p className="text-xs font-mono text-error-500 break-all">
                  {this.state.error.message}
                </p>
              </div>
            )}
            <div className="flex gap-3 justify-center">
              <Button variant="outline" onClick={this.handleGoBack}>
                <ArrowLeft className="w-4 h-4 mr-2" />
                {t("shared.errorBoundary.goBack")}
              </Button>
              <Button onClick={this.handleRetry}>
                <RefreshCw className="w-4 h-4 mr-2" />
                {t("shared.errorBoundary.tryAgain")}
              </Button>
            </div>
          </div>
        </div>
      );
    }
    return this.props.children;
  }
}

export class ErrorBoundary extends React.Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = {
      hasError: false,
      error: null,
      errorInfo: null,
      copied: false,
    };
  }

  static getDerivedStateFromError(error: Error): Partial<ErrorBoundaryState> {
    return { hasError: true, error };
  }

  override componentDidCatch(error: Error, errorInfo: React.ErrorInfo) {
    if (import.meta.env.DEV) {
      console.error("ErrorBoundary caught an error:", error, errorInfo);
    }
    this.setState({ error, errorInfo });
  }

  handleReset = () => {
    this.setState({ hasError: false, error: null, errorInfo: null });
    window.location.reload();
  };

  handleGoHome = () => {
    window.location.href = "/";
  };

  handleCopyError = () => {
    const errorText = `
Error: ${this.state.error?.message}
Stack: ${this.state.error?.stack}
Component Stack: ${this.state.errorInfo?.componentStack}
    `.trim();

    navigator.clipboard.writeText(errorText).then(() => {
      this.setState({ copied: true });
      toast.success(t("toast.errorCopied"));
      setTimeout(() => this.setState({ copied: false }), 2000);
    });
  };

  override render() {
    if (this.state.hasError) {
      return (
        <div className="min-h-screen flex items-center justify-center bg-[#0B0F1A] p-4 font-sans selection:bg-brand-500/30">
          <div className="absolute top-0 left-0 w-full h-full overflow-hidden pointer-events-none">
            <div className="absolute top-[-10%] left-[-10%] w-[40%] h-[40%] bg-error-500/10 blur-[120px] rounded-full" />
            <div className="absolute bottom-[-10%] right-[-10%] w-[40%] h-[40%] bg-brand-500/10 blur-[120px] rounded-full" />
          </div>

          <div className="max-w-3xl w-full relative">
            <div className="bg-[#151926]/80 backdrop-blur-xl border border-white/5 rounded-2xl shadow-2xl overflow-hidden">
              <div className="relative h-2 bg-gradient-to-r from-error-500 via-brand-500 to-success-500" />

              <div className="p-8 sm:p-12">
                <div className="flex flex-col items-center text-center space-y-6">
                  <div className="relative">
                    <div className="absolute inset-0 bg-error-500/20 blur-2xl rounded-full scale-150 animate-pulse" />
                    <div className="relative w-24 h-24 rounded-3xl bg-error-500/10 border border-error-500/20 flex items-center justify-center rotate-3 hover:rotate-0 transition-transform duration-500">
                      <TriangleAlert className="w-12 h-12 text-error-500" />
                    </div>
                  </div>

                  <div className="space-y-2">
                    <h1 className="text-3xl sm:text-4xl font-bold text-white tracking-tight">
                      {t("shared.errorBoundary.appTitle")}
                    </h1>
                    <p className="text-slate-400 text-lg max-w-md mx-auto">
                      {t("shared.errorBoundary.appDescription")}
                    </p>
                  </div>
                </div>

                <div className="mt-12 space-y-4">
                  <div className="flex items-center justify-between px-2">
                    <span className="text-xs font-bold text-slate-500 uppercase tracking-widest">
                      {t("shared.errorBoundary.technicalDetails")}
                    </span>
                    <button
                      onClick={this.handleCopyError}
                      className="flex items-center gap-2 text-xs text-brand-400 hover:text-brand-300 transition-colors uppercase font-bold tracking-widest"
                    >
                      {this.state.copied ? <Check className="w-3 h-3" /> : <Copy className="w-3 h-3" />}
                      {this.state.copied
                        ? t("shared.errorBoundary.copiedShort")
                        : t("shared.errorBoundary.copyDetails")}
                    </button>
                  </div>

                  <div className="bg-[#0B0F1A]/50 border border-white/5 rounded-xl p-6 font-mono text-sm group relative overflow-hidden">
                    <div className="absolute top-0 left-0 w-1 h-full bg-error-500/50" />
                    <div className="space-y-2 relative z-10">
                      <p className="text-error-400 font-bold">
                        {this.state.error?.name || "Error"}:{" "}
                        {this.state.error?.message || t("shared.errorBoundary.unknownError")}
                      </p>

                      {import.meta.env.DEV && this.state.errorInfo && (
                        <div className="mt-4 pt-4 border-t border-white/5 max-h-48 overflow-auto custom-scrollbar text-slate-500 text-xs leading-relaxed">
                          {this.state.errorInfo.componentStack}
                        </div>
                      )}
                    </div>
                  </div>
                </div>

                <div className="mt-10 grid grid-cols-1 sm:grid-cols-2 gap-4">
                  <button
                    onClick={this.handleReset}
                    className="flex items-center justify-center gap-2 h-14 bg-white text-black font-bold rounded-xl hover:bg-slate-200 transition-all active:scale-[0.98]"
                  >
                    <RefreshCw className="w-5 h-5" />
                    {t("shared.errorBoundary.reloadPage")}
                  </button>
                  <button
                    onClick={this.handleGoHome}
                    className="flex items-center justify-center gap-2 h-14 bg-[#1F2433] text-white font-bold rounded-xl hover:bg-[#2A2F3D] transition-all border border-white/5 active:scale-[0.98]"
                  >
                    <Home className="w-5 h-5" />
                    {t("shared.errorBoundary.goHome")}
                  </button>
                </div>

                <div className="mt-12 pt-8 border-t border-white/5 text-center">
                  <p className="text-slate-500 text-sm">{t("shared.errorBoundary.persistHelp")}</p>
                </div>
              </div>
            </div>

            <div className="mt-6 flex justify-center items-center gap-3">
              <span className="px-3 py-1 bg-white/5 rounded-full text-[10px] font-bold text-slate-500 uppercase tracking-tighter border border-white/5">
                {import.meta.env.VITE_APP_VERSION || 'v1.0.0'}
              </span>
              <span className="px-3 py-1 bg-brand-500/10 rounded-full text-[10px] font-bold text-brand-500 uppercase tracking-tighter border border-brand-500/20">
                {import.meta.env.MODE}
              </span>
            </div>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
