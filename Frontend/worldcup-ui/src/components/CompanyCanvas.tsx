import { useEffect, useRef } from "react";
import * as Phaser from "phaser";
import { CompanyScene } from "../game/companyScene";
import type { CompanyState, Match, SceneSelection } from "../types";

type CompanyCanvasProps = {
  state: CompanyState | null;
  selectedMatch: Match | null;
  onSelection: (selection: SceneSelection) => void;
};

export function CompanyCanvas({ state, selectedMatch, onSelection }: CompanyCanvasProps) {
  const hostRef = useRef<HTMLDivElement | null>(null);
  const gameRef = useRef<Phaser.Game | null>(null);

  useEffect(() => {
    if (!hostRef.current || gameRef.current) return;

    const game = new Phaser.Game({
      type: Phaser.AUTO,
      parent: hostRef.current,
      width: 1280,
      height: 720,
      backgroundColor: "#141d1a",
      antialias: true,
      scale: {
        mode: Phaser.Scale.RESIZE,
        autoCenter: Phaser.Scale.CENTER_BOTH
      },
      scene: [CompanyScene]
    });

    game.events.on("company-selection", onSelection);
    gameRef.current = game;

    return () => {
      game.events.off("company-selection", onSelection);
      game.destroy(true);
      gameRef.current = null;
    };
  }, [onSelection]);

  useEffect(() => {
    if (!gameRef.current || !state) return;
    const scene = gameRef.current.scene.getScene("CompanyScene") as CompanyScene;
    scene.setCompanyState(state, selectedMatch);
  }, [state, selectedMatch]);

  return <div className="company-canvas" ref={hostRef} />;
}
