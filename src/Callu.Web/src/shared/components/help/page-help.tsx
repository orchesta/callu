import { useLocation } from "react-router";
import { HelpCircle } from "lucide-react";
import {
  Sheet,
  SheetTrigger,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
} from "@/shared/components/ui/sheet";
import { t } from "@/shared/locales/i18n";

/**
 * Maps the current route to a help-guide key. First match wins, so list the more
 * specific paths before their parents (e.g. an incident detail before the list).
 * Guide copy lives in the locale files under `help.<key>.*`.
 */
const GUIDE_ROUTES: { test: RegExp; key: string }[] = [
  { test: /^\/dashboard/, key: "dashboard" },
  { test: /^\/incidents\/[^/]+$/, key: "incidentDetail" },
  { test: /^\/incidents/, key: "incidents" },
  { test: /^\/conferences/, key: "conferences" },
  { test: /^\/services/, key: "services" },
  { test: /^\/escalations/, key: "escalations" },
  { test: /^\/schedules/, key: "schedules" },
  { test: /^\/teams/, key: "teams" },
  { test: /^\/reports/, key: "reports" },
  { test: /^\/postmortems/, key: "postmortems" },
  { test: /^\/runbooks/, key: "runbooks" },
  { test: /^\/call-logs/, key: "callLogs" },
  { test: /^\/maintenance/, key: "maintenance" },
  { test: /^\/users/, key: "users" },
  { test: /^\/settings\/communications/, key: "communications" },
  { test: /^\/settings\/email-templates/, key: "emailTemplates" },
  { test: /^\/settings\/status-page/, key: "statusPage" },
  { test: /^\/settings/, key: "settings" },
  { test: /^\/notifications/, key: "notifications" },
  { test: /^\/profile/, key: "profile" },
];

function resolveGuideKey(pathname: string): string | null {
  return GUIDE_ROUTES.find((g) => g.test.test(pathname))?.key ?? null;
}

/**
 * Per-page help. Renders a circular "?" button in the app header that opens a
 * right-side drawer explaining what the current page does and how to use it.
 * Renders nothing on pages that have no guide.
 */
export function PageHelp() {
  const { pathname } = useLocation();
  const key = resolveGuideKey(pathname);
  if (!key) return null;

  const base = `help.${key}`;
  const steps = t(`${base}.steps`)
    .split("\n")
    .map((s) => s.trim())
    .filter(Boolean);
  const tip = t(`${base}.tip`);
  const hasTip = tip !== `${base}.tip` && tip.trim().length > 0;

  return (
    <Sheet>
      <SheetTrigger asChild>
        <button
          type="button"
          aria-label={t("help.button")}
          title={t("help.button")}
          className="inline-flex items-center justify-center w-9 h-9 rounded-full text-muted-foreground hover:text-brand-500 hover:bg-muted transition-colors"
        >
          <HelpCircle className="w-5 h-5" />
        </button>
      </SheetTrigger>

      <SheetContent side="right" className="w-full sm:max-w-md overflow-y-auto">
        <SheetHeader>
          <SheetTitle className="text-lg">{t(`${base}.title`)}</SheetTitle>
          <SheetDescription>{t("help.heading")}</SheetDescription>
        </SheetHeader>

        <div className="px-4 pb-8 space-y-6">
          <section className="space-y-1.5">
            <h3 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
              {t("help.whatHeading")}
            </h3>
            <p className="text-sm leading-relaxed text-foreground">{t(`${base}.summary`)}</p>
          </section>

          <section className="space-y-2">
            <h3 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
              {t("help.howHeading")}
            </h3>
            <ol className="space-y-2.5 text-sm leading-relaxed">
              {steps.map((step) => (
                <li key={step} className="flex gap-3">
                  <span className="flex-shrink-0 mt-0.5 w-5 h-5 rounded-full bg-brand-500/15 text-brand-500 text-xs font-semibold flex items-center justify-center">
                    •
                  </span>
                  <span className="text-foreground">{step}</span>
                </li>
              ))}
            </ol>
          </section>

          {hasTip && (
            <section className="rounded-lg border border-brand-500/20 bg-brand-500/5 p-3 space-y-1">
              <h3 className="text-xs font-semibold uppercase tracking-wider text-brand-500">
                {t("help.tipHeading")}
              </h3>
              <p className="text-sm leading-relaxed text-foreground">{tip}</p>
            </section>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
