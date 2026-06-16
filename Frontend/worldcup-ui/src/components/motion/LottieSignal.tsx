import { lazy, Suspense } from "react";
import aiOrbit from "../../animations/ai-orbit.json";
import dataPulse from "../../animations/data-pulse.json";
import evidenceFlow from "../../animations/evidence-flow.json";

const Lottie = lazy(() => import("lottie-react"));

type LottieSignalVariant = "data" | "ai" | "evidence";

const animations = {
  data: dataPulse,
  ai: aiOrbit,
  evidence: evidenceFlow
};

export function LottieSignal({
  variant,
  label,
  size = "md"
}: {
  variant: LottieSignalVariant;
  label: string;
  size?: "sm" | "md" | "lg" | "wide";
}) {
  return (
    <span className={`lottie-signal ${variant} ${size}`} aria-label={label} title={label}>
      <Suspense fallback={<span className="lottie-fallback" />}>
        <Lottie animationData={animations[variant]} loop autoplay />
      </Suspense>
    </span>
  );
}
