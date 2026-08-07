import { useLocation, Link } from "react-router";
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

import { resolveGuideKey, relatedPages } from "./guide-routes";

/** Header "?" button opening a drawer that explains the current page, or nothing when it has no guide. */
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
  const pitfall = t(`${base}.pitfall`);
  const hasPitfall = pitfall !== `${base}.pitfall` && pitfall.trim().length > 0;
  const related = relatedPages(key);

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

          {hasPitfall && (
            <section className="rounded-lg border border-warning-500/20 bg-warning-500/5 p-3 space-y-1">
              <h3 className="text-xs font-semibold uppercase tracking-wider text-warning-500">
                {t("help.pitfallHeading")}
              </h3>
              <p className="text-sm leading-relaxed text-foreground">{pitfall}</p>
            </section>
          )}

          {related.length > 0 && (
            <section className="space-y-2">
              <h3 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                {t("help.relatedHeading")}
              </h3>
              <div className="flex flex-wrap gap-2">
                {related.map((page) => (
                  <Link
                    key={page.to}
                    to={page.to}
                    className="inline-flex items-center rounded-md border border-border px-2.5 py-1 text-sm text-foreground hover:border-brand-500 hover:text-brand-500 transition-colors"
                  >
                    {t(page.labelKey)}
                  </Link>
                ))}
              </div>
            </section>
          )}

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
