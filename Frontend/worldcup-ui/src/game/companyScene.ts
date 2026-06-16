import * as Phaser from "phaser";
import type { Assignment, CompanyState, Employee, Match, SceneSelection, WatchObject } from "../types";
import { teamShortName } from "../utils/teamNames";

type SeatRecord = {
  objectId: string;
  employeeId: string | null;
  container: Phaser.GameObjects.Container;
  aura: Phaser.GameObjects.Ellipse;
  desk: Phaser.GameObjects.Image;
  chair: Phaser.GameObjects.Image;
  nameTag: Phaser.GameObjects.Text;
};

type ProgressStage = "pool" | "advance" | "knockout" | "final" | "champion";

type SeatPosition = {
  x: number;
  y: number;
  region: number;
  stage: ProgressStage;
  laneColor: number;
};

const WORLD = {
  width: 1940,
  height: 2160,
  cx: 970,
  cy: 1080
};

const COLORS = {
  bg: 0x111713,
  floor: 0xd7c5a5,
  floorDark: 0xb79f7c,
  wall: 0x26382f,
  ink: 0x14201b,
  muted: 0x697267,
  cream: 0xfff0cc,
  paper: 0xf7e7c4,
  gold: 0xe4b94f,
  red: 0xb8423f,
  green: 0x3c8b62,
  blue: 0x3d6d9f,
  teal: 0x328b86,
  purple: 0x8063a8,
  gray: 0x8d8f89
};

const REGION_COLORS = [COLORS.green, COLORS.blue, COLORS.red, COLORS.purple];
const REGION_NAMES = ["一区", "二区", "三区", "四区"];

const MANAGEMENT_SEATS = [
  { id: "ceo_room", label: "CEO", x: 1656, y: 188, color: COLORS.gold },
  { id: "data_room", label: "DATA", x: 1744, y: 258, color: COLORS.teal },
  { id: "risk_room", label: "RISK", x: 1744, y: 940, color: COLORS.red },
  { id: "hr_room", label: "OPS", x: 1656, y: 1012, color: COLORS.purple }
];

const poolSlots = [
  { x: 0, y: 0 }, { x: 148, y: 0 }, { x: 296, y: 0 },
  { x: 0, y: 106 }, { x: 148, y: 106 }, { x: 296, y: 106 },
  { x: 0, y: 212 }, { x: 148, y: 212 }, { x: 296, y: 212 },
  { x: 0, y: 318 }, { x: 148, y: 318 }, { x: 296, y: 318 }
];

const advanceSlots = [
  { x: 0, y: 0 }, { x: 126, y: 0 }, { x: 0, y: 104 }, { x: 126, y: 104 }, { x: 0, y: 208 }, { x: 126, y: 208 }
];

export class CompanyScene extends Phaser.Scene {
  private companyState: CompanyState | null = null;
  private selectedMatch: Match | null = null;
  private seats = new Map<string, SeatRecord>();
  private eventLayer?: Phaser.GameObjects.Container;
  private highlightLayer?: Phaser.GameObjects.Container;
  private officeRoot?: Phaser.GameObjects.Container;
  private focusRing?: Phaser.GameObjects.Ellipse;
  private cameraDragStart?: { x: number; y: number; scrollX: number; scrollY: number };

  constructor() {
    super("CompanyScene");
  }

  preload() {
    this.load.svg("asset-plant", "/worldcup-company-assets/plant.svg", { width: 52, height: 60 });
    this.load.image("asset-desk-soft", "/worldcup-company-assets/desk.svg");
    this.load.image("asset-researcher-soft", "/worldcup-company-assets/researcher.svg");
    this.load.image("asset-executive-soft", "/worldcup-company-assets/executive.svg");
    this.load.image("asset-trophy-soft", "/worldcup-company-assets/trophy.svg");
  }

  create() {
    this.cameras.main.setBackgroundColor("#111713");
    this.cameras.main.setBounds(0, 0, WORLD.width, WORLD.height);
    this.input.mouse?.disableContextMenu();
    this.buildOfficeFloor();
    this.setupCameraInput();
    if (this.companyState) this.renderCompany();
  }

  setCompanyState(state: CompanyState, selectedMatch: Match | null) {
    this.companyState = state;
    this.selectedMatch = selectedMatch;
    if (this.scene.isActive()) this.renderCompany();
  }

  private buildOfficeFloor() {
    this.officeRoot?.destroy(true);
    this.officeRoot = this.add.container(0, 0);

    this.drawFloor();
    this.drawTournamentLanes();
    this.drawForwardWorkstations();
    this.drawManagementPods();
    this.drawQuietDecor();

    this.highlightLayer = this.add.container(0, 0);
    this.eventLayer = this.add.container(0, 0);
  }

  private drawFloor() {
    const shadow = this.add.rectangle(WORLD.cx + 12, WORLD.cy + 16, 1778, 1980, 0x0c1210, 0.38);
    const floor = this.add.rectangle(WORLD.cx, WORLD.cy, 1778, 1980, 0xe8ebf1, 1).setStrokeStyle(2, 0xf4f6fb, 1);
    const tint = this.add.rectangle(WORLD.cx, WORLD.cy, 1750, 1950, 0xf7f8fb, 0.82);
    const glow = this.add.ellipse(WORLD.cx, 610, 1220, 760, 0xffffff, 0.12);
    this.officeRoot?.add([shadow, floor, tint, glow]);

    for (let x = 170; x <= 1770; x += 116) {
      this.officeRoot?.add(this.add.rectangle(x, WORLD.cy, 2, 1900, 0xdde5f0, 0.36));
    }
    for (let y = 132; y <= 2040; y += 80) {
      this.officeRoot?.add(this.add.rectangle(WORLD.cx, y, 1740, 2, 0xdde5f0, 0.42));
    }

    const title = this.add.text(148, 70, "世界杯 AI 公司", {
      fontFamily: "Arial, Microsoft YaHei, sans-serif",
      fontSize: "28px",
      color: "#5a86cc",
      fontStyle: "bold"
    });
    const subtitle = this.add.text(150, 106, "一个球队一名员工，小组工位出发，晋级后搬到前排工位。", {
      fontFamily: "Arial, Microsoft YaHei, sans-serif",
      fontSize: "16px",
      color: "#7d8ea6"
    });
    this.officeRoot?.add([title, subtitle]);
  }

  private drawTournamentLanes() {
    this.getRegionOrigins().forEach((origin, region) => {
      const color = REGION_COLORS[region];
      const lane = this.add.rectangle(716, origin.y + 156, 1250, 446, 0xffffff, 0.48).setStrokeStyle(1, color, 0.12);
      const strip = this.add.rectangle(106, origin.y + 156, 8, 406, color, 0.72);
      const label = this.add.text(126, origin.y - 34, REGION_NAMES[region], {
        fontFamily: "Arial, Microsoft YaHei, sans-serif",
        fontSize: "18px",
        color: "#51667c",
        fontStyle: "bold"
      });
      const stage = this.add.text(700, origin.y - 34, "晋级工位", {
        fontFamily: "Arial, Microsoft YaHei, sans-serif",
        fontSize: "15px",
        color: "#8a97a9"
      });
      this.officeRoot?.add([lane, strip, label, stage]);
    });
  }

  private drawForwardWorkstations() {
    this.getRegionOrigins().forEach((origin, region) => {
      const color = REGION_COLORS[region];

      advanceSlots.forEach((slot, index) => {
        this.drawGhostDesk(origin.x + 596 + slot.x, origin.y + slot.y, color, index < 2 ? "16强" : index < 4 ? "8强" : "4强");
      });

      this.drawGhostDesk(origin.x + 900, origin.y + 80, color, "决赛");
      this.drawGhostDesk(origin.x + 900, origin.y + 184, color, "决赛");
    });

    const championY = 760;
    const championGlow = this.add.ellipse(1596, championY, 178, 280, COLORS.gold, 0.08).setStrokeStyle(2, COLORS.gold, 0.22);
    const championDesk = this.add.image(1596, championY, "asset-trophy-soft").setScale(0.55).setAlpha(0.92);
    const championLabel = this.add.text(1596, championY + 70, "冠军工位", {
      fontFamily: "Arial, Microsoft YaHei, sans-serif",
      fontSize: "13px",
      color: "#8c7c50",
      fontStyle: "bold"
    }).setOrigin(0.5);
    this.officeRoot?.add([championGlow, championDesk, championLabel]);
  }

  private drawGhostDesk(x: number, y: number, color: number, label: string) {
    const pad = this.add.ellipse(x, y + 34, 132, 68, color, 0.035).setStrokeStyle(1, color, 0.16);
    const desk = this.add.image(x, y + 20, "asset-desk-soft").setScale(0.17).setAlpha(0.78);
    const chair = this.add.image(x - 42, y + 30, "asset-desk-soft").setScale(0.09).setAlpha(0.28);
    const text = this.add.text(x, y - 18, label, {
      fontFamily: "Arial, Microsoft YaHei, sans-serif",
      fontSize: "13px",
      color: "#8895a9",
      fontStyle: "bold"
    }).setOrigin(0.5);
    this.officeRoot?.add([pad, desk, chair, text]);
  }

  private drawManagementPods() {
    MANAGEMENT_SEATS.forEach((seat) => {
      const pod = this.add.container(seat.x, seat.y);
      const aura = this.add.ellipse(0, 20, 118, 76, seat.color, 0.08).setStrokeStyle(1, seat.color, 0.22);
      const desk = this.add.image(0, 22, "asset-desk-soft").setScale(0.18).setAlpha(0.96);
      const person = this.drawPerson(0, -22, seat.color, false, true);
      const label = this.add.text(0, 48, seat.label, {
        fontFamily: "Arial, sans-serif",
        fontSize: "12px",
        color: "#75879f",
        fontStyle: "bold"
      }).setOrigin(0.5);
      pod.add([aura, desk, ...person, label]);
      pod.setInteractive(new Phaser.Geom.Rectangle(-45, -42, 90, 106), Phaser.Geom.Rectangle.Contains);
      pod.on("pointerdown", () => this.emitSelection({ type: "room", roomId: seat.id }));
      this.officeRoot?.add(pod);
    });
  }

  private drawQuietDecor() {
    [[116, 166], [116, 1056], [1816, 166], [1816, 1056]].forEach(([x, y]) => {
      this.officeRoot?.add(this.add.image(x, y, "asset-plant").setAlpha(0.72));
    });
  }

  private setupCameraInput() {
    const camera = this.cameras.main;
    camera.setZoom(0.86);
    camera.centerOn(720, 520);

    this.input.on("wheel", (_pointer: unknown, _gameObjects: unknown, _dx: number, dy: number) => {
      const nextZoom = Phaser.Math.Clamp(camera.zoom + (dy > 0 ? -0.06 : 0.06), 0.64, 1.35);
      camera.setZoom(nextZoom);
    });

    this.input.on("pointerdown", (pointer: Phaser.Input.Pointer) => {
      if (pointer.rightButtonDown() || pointer.middleButtonDown()) {
        this.cameraDragStart = { x: pointer.x, y: pointer.y, scrollX: camera.scrollX, scrollY: camera.scrollY };
      }
    });

    this.input.on("pointermove", (pointer: Phaser.Input.Pointer) => {
      if (!this.cameraDragStart || !pointer.isDown) return;
      camera.scrollX = this.cameraDragStart.scrollX + (this.cameraDragStart.x - pointer.x) / camera.zoom;
      camera.scrollY = this.cameraDragStart.scrollY + (this.cameraDragStart.y - pointer.y) / camera.zoom;
    });

    this.input.on("pointerup", () => {
      this.cameraDragStart = undefined;
    });
  }

  private renderCompany() {
    if (!this.companyState) return;
    this.seats.forEach((seat) => seat.container.destroy(true));
    this.seats.clear();
    this.highlightLayer?.removeAll(true);
    this.eventLayer?.removeAll(true);

    const assignmentByObject = new Map<string, Assignment>();
    this.companyState.assignments.forEach((assignment) => {
      if (assignment.assignment_role === "primary_researcher") assignmentByObject.set(assignment.object_id, assignment);
    });
    const employeeById = new Map<string, Employee>(this.companyState.employees.map((employee) => [employee.id, employee]));
    const signalsByObject = new Map<string, number>();
    this.companyState.signals.forEach((signal) => {
      if (signal.object_id) signalsByObject.set(signal.object_id, (signalsByObject.get(signal.object_id) ?? 0) + 1);
    });

    this.companyState.teams.slice(0, 48).forEach((team, index) => {
      const assignment = assignmentByObject.get(team.id);
      const employee = assignment ? employeeById.get(assignment.employee_id) : undefined;
      this.createSeat(team, employee ?? null, index, signalsByObject.get(team.id) ?? 0);
    });

    this.renderMatchFocus();
    this.renderEventTicker();
  }

  private createSeat(team: WatchObject, employee: Employee | null, index: number, signalCount: number) {
    const position = this.getSeatPosition(team, index);
    const offboarded = team.status === "eliminated";
    const color = offboarded ? COLORS.gray : position.laneColor;
    const container = this.add.container(position.x, position.y);
    const alpha = offboarded ? 0.42 : 1;

    const aura = this.add.ellipse(0, 44, 142, 76, color, offboarded ? 0.04 : 0.08).setStrokeStyle(1, color, offboarded ? 0.12 : 0.22);
    const cable = this.add.line(0, 0, -50, 56, 52, 56, 0x9aa6b7, offboarded ? 0.14 : 0.28).setLineWidth(2, 1);
    const desk = this.add.image(12, 30, "asset-desk-soft").setScale(0.18).setAlpha(offboarded ? 0.32 : 0.96);
    const chair = this.add.image(-46, 40, "asset-desk-soft").setScale(0.1).setAlpha(offboarded ? 0.25 : 0.48);
    const laptop = this.add.rectangle(22, 30, 28, 16, COLORS.ink, offboarded ? 0.18 : 0.56);
    const person = this.drawPerson(-48, -8, color, offboarded, false);
    const nameTag = this.add.text(8, 70, this.shortTeamName(team), {
      fontFamily: "Arial, Microsoft YaHei, sans-serif",
      fontSize: "16px",
      color: offboarded ? "#7a7f87" : "#3f4d60",
      fontStyle: "bold"
    }).setOrigin(0.5);
    const status = this.add.circle(58, 4, 6, color, offboarded ? 0.32 : 0.92);

    container.add([aura, cable, desk, chair, laptop, ...person, nameTag, status]);
    container.setAlpha(alpha);

    if (signalCount > 0) {
      const pulse = this.add.circle(68, -24, 11, COLORS.red, 0.9);
      const count = this.add.text(68, -24, String(Math.min(signalCount, 9)), {
        fontFamily: "Arial, sans-serif",
        fontSize: "11px",
        color: "#ffffff",
        fontStyle: "bold"
      }).setOrigin(0.5);
      container.add([pulse, count]);
      this.tweens.add({ targets: pulse, scale: 1.3, alpha: 0.42, duration: 820, yoyo: true, repeat: -1 });
    }

    container.setInteractive(new Phaser.Geom.Rectangle(-72, -42, 144, 128), Phaser.Geom.Rectangle.Contains);
    container.on("pointerdown", () => {
      if (!employee) return;
      this.focusSeat(team.id);
      this.emitSelection({ type: "employee", employeeId: employee.id, objectId: team.id });
    });
    container.on("pointerover", () => {
      aura.setAlpha(offboarded ? 0.08 : 0.16);
      desk.setAlpha(offboarded ? 0.42 : 1);
      chair.setAlpha(offboarded ? 0.3 : 0.62);
      this.tweens.add({ targets: container, y: position.y - 3, duration: 120, ease: "Sine.easeOut" });
    });
    container.on("pointerout", () => {
      aura.setAlpha(offboarded ? 0.04 : 0.08);
      desk.setAlpha(offboarded ? 0.32 : 0.96);
      chair.setAlpha(offboarded ? 0.25 : 0.5);
      this.tweens.add({ targets: container, y: position.y, duration: 150, ease: "Sine.easeOut" });
    });

    this.seats.set(team.id, { objectId: team.id, employeeId: employee?.id ?? null, container, aura, desk, chair, nameTag });
  }

  private drawPerson(x: number, y: number, color: number, muted: boolean, executive = false) {
    const sprite = this.add.image(x, y, executive ? "asset-executive-soft" : "asset-researcher-soft").setScale(executive ? 0.26 : 0.24);
    if (muted) sprite.setAlpha(0.5).setTint(0xaeb7c4);
    else sprite.setTint(color);
    const halo = this.add.ellipse(x, y + 26, executive ? 54 : 48, executive ? 30 : 28, color, muted ? 0.08 : 0.12);
    return [halo, sprite];
  }

  private shortTeamName(team: WatchObject) {
    return teamShortName(team, team.symbol);
  }

  private getSeatPosition(team: WatchObject, index: number): SeatPosition {
    const region = Math.floor(index / 12);
    const regionIndex = index % 12;
    const origin = this.getRegionOrigins()[region] ?? this.getRegionOrigins()[0];
    const laneColor = REGION_COLORS[region] ?? COLORS.green;
    const stage = this.getProgressStage(team);

    if (stage === "champion") return { x: 1596, y: 760, region, stage, laneColor };
    if (stage === "final") {
      const y = origin.y + (regionIndex % 2 === 0 ? 42 : 124);
      return { x: origin.x + 804, y, region, stage, laneColor };
    }
    if (stage === "knockout") {
      const slot = advanceSlots[regionIndex % advanceSlots.length];
      return { x: origin.x + 548 + slot.x, y: origin.y + slot.y, region, stage, laneColor };
    }
    if (stage === "advance") {
      const slot = advanceSlots[regionIndex % advanceSlots.length];
      return { x: origin.x + 548 + slot.x, y: origin.y + slot.y, region, stage, laneColor };
    }

    const slot = poolSlots[regionIndex];
    return { x: origin.x + slot.x, y: origin.y + slot.y, region, stage, laneColor };
  }

  private getProgressStage(team: WatchObject): ProgressStage {
    const raw = `${team.status} ${team.metadata_json}`.toLowerCase();
    if (raw.includes("champion")) return "champion";
    if (raw.includes("final")) return "final";
    if (raw.includes("semi") || raw.includes("quarter") || raw.includes("knockout")) return "knockout";
    if (raw.includes("advanced") || raw.includes("qualified")) return "advance";
    return "pool";
  }

  private getRegionOrigins() {
    return [
      { x: 182, y: 190 },
      { x: 182, y: 670 },
      { x: 182, y: 1150 },
      { x: 182, y: 1630 }
    ];
  }

  private renderMatchFocus() {
    if (!this.selectedMatch) return;
    [this.selectedMatch.home_object_id, this.selectedMatch.away_object_id].forEach((objectId, index) => {
      const seat = this.seats.get(objectId);
      if (!seat) return;
      const ring = this.add.ellipse(seat.container.x, seat.container.y + 24, 132, 78).setStrokeStyle(4, index === 0 ? COLORS.blue : COLORS.red, 0.92);
      const tag = this.add.text(seat.container.x, seat.container.y - 54, index === 0 ? "HOME" : "AWAY", {
        fontFamily: "Arial, sans-serif",
        fontSize: "11px",
        color: "#fff7df",
        fontStyle: "bold",
        backgroundColor: index === 0 ? "#3d6d9f" : "#b8423f",
        padding: { x: 6, y: 3 }
      }).setOrigin(0.5);
      this.highlightLayer?.add([ring, tag]);
      this.tweens.add({ targets: ring, scale: 1.12, alpha: 0.5, duration: 850, yoyo: true, repeat: -1 });
    });
  }

  private renderEventTicker() {
    if (!this.companyState) return;
    this.companyState.logs.slice(0, 3).forEach((log, index) => {
      const x = 1262;
      const y = 88 + index * 34;
      const row = this.add.container(x, y);
      const dot = this.add.circle(0, 0, 5, this.logColor(log.category), 0.95);
      const text = this.add.text(14, -9, `${this.logLabel(log.category)} ${log.title || log.message || log.event_type}`, {
        fontFamily: "Microsoft YaHei, Arial, sans-serif",
        fontSize: "13px",
        color: "#ffecc4",
        fixedWidth: 500
      }).setCrop(0, 0, 500, 20);
      row.add([dot, text]);
      row.setInteractive(new Phaser.Geom.Rectangle(-8, -14, 540, 28), Phaser.Geom.Rectangle.Contains);
      row.on("pointerdown", () => this.emitSelection({ type: "event", logId: log.id }));
      this.eventLayer?.add(row);
    });
  }

  private focusSeat(objectId: string) {
    const seat = this.seats.get(objectId);
    if (!seat) return;
    this.focusRing?.destroy();
    this.focusRing = this.add.ellipse(seat.container.x, seat.container.y + 24, 150, 92).setStrokeStyle(5, 0xffffff, 0.92);
    this.tweens.add({
      targets: this.focusRing,
      alpha: 0,
      scale: 1.3,
      duration: 680,
      onComplete: () => this.focusRing?.destroy()
    });
    this.cameras.main.pan(seat.container.x, seat.container.y, 420, "Sine.easeInOut");
  }

  private emitSelection(selection: SceneSelection) {
    this.game.events.emit("company-selection", selection);
  }

  private logLabel(category: string) {
    const labels: Record<string, string> = {
      data: "DATA",
      intelligence: "INTEL",
      employee: "STAFF",
      workflow: "FLOW",
      llm: "LLM",
      system: "SYS"
    };
    return labels[category] ?? category.toUpperCase();
  }

  private logColor(category: string) {
    const colors: Record<string, number> = {
      data: COLORS.blue,
      intelligence: COLORS.gold,
      employee: COLORS.green,
      workflow: COLORS.teal,
      llm: COLORS.purple,
      system: COLORS.gray
    };
    return colors[category] ?? COLORS.gold;
  }
}
