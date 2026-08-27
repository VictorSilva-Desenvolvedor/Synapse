import { App, Plugin, PluginSettingTab, Setting, Notice, setIcon } from 'obsidian';
import * as net from 'net';

interface IpcEnvelope {
    versao: number;
    tipo: string;
    payload?: any;
}

interface IpcStatusPayload {
    estado: string;
    pausado: boolean;
    ultimaSincronizacaoEm?: string | null;
    itensPendentes: number;
}

const PIPE_PATH = '\\\\.\\pipe\\synapse-ipc';

export default class SynapsePlugin extends Plugin {
    private statusBarItem: HTMLElement | null = null;
    private pollIntervalId: number | null = null;
    private isPaused = false;
    private currentStatus = 'Desconectado';

    async onload() {
        console.log('Carregando Synapse Obsidian Plugin...');

        // 1. Cria item na Status Bar
        this.statusBarItem = this.addStatusBarItem();
        this.updateStatusBar('Desconectado', false, 0);

        // 2. Registra Comandos no Obsidian Command Palette
        this.addCommand({
            id: 'synapse-toggle-pause',
            name: 'Pausar/Retomar Sincronização',
            callback: async () => {
                await this.togglePause();
            }
        });

        this.addCommand({
            id: 'synapse-reconnect',
            name: 'Reconectar ao GitHub / Forçar Verificação',
            callback: async () => {
                await this.reconnect();
            }
        });

        this.addCommand({
            id: 'synapse-check-status',
            name: 'Verificar Status de Sincronização',
            callback: async () => {
                await this.fetchStatus();
                new Notice(`Synapse: ${this.currentStatus}`);
            }
        });

        // 3. Inicia polling periódico (a cada 3.5 segundos)
        this.pollIntervalId = window.setInterval(async () => {
            await this.fetchStatus();
        }, 3500);

        // Primeira consulta imediata
        await this.fetchStatus();
    }

    onunload() {
        if (this.pollIntervalId !== null) {
            window.clearInterval(this.pollIntervalId);
            this.pollIntervalId = null;
        }
    }

    private sendIpcCommand(tipo: string, payload: any = null): Promise<IpcEnvelope | null> {
        return new Promise((resolve) => {
            try {
                const client = net.createConnection(PIPE_PATH, () => {
                    const envelope = {
                        versao: 1,
                        tipo: tipo,
                        payload: payload
                    };
                    client.write(JSON.stringify(envelope) + '\n');
                });

                let buffer = '';

                client.on('data', (data) => {
                    buffer += data.toString('utf8');
                    if (buffer.includes('\n')) {
                        const line = buffer.split('\n')[0].trim();
                        try {
                            const response: IpcEnvelope = JSON.parse(line);
                            client.end();
                            resolve(response);
                        } catch {
                            client.end();
                            resolve(null);
                        }
                    }
                });

                client.on('error', () => {
                    resolve(null);
                });

                client.setTimeout(2000, () => {
                    client.destroy();
                    resolve(null);
                });
            } catch {
                resolve(null);
            }
        });
    }

    private async fetchStatus() {
        const response = await this.sendIpcCommand('GetStatus');
        if (response && response.payload) {
            const status: IpcStatusPayload = response.payload;
            this.currentStatus = status.estado;
            this.isPaused = status.pausado;
            this.updateStatusBar(status.estado, status.pausado, status.itensPendentes, status.ultimaSincronizacaoEm);
        } else {
            this.currentStatus = 'Serviço Offline';
            this.updateStatusBar('Serviço Offline', false, 0);
        }
    }

    private async togglePause() {
        const command = this.isPaused ? 'Resume' : 'Pause';
        const response = await this.sendIpcCommand(command);
        if (response && response.payload) {
            const status: IpcStatusPayload = response.payload;
            this.isPaused = status.pausado;
            new Notice(this.isPaused ? 'Synapse: Sincronização Pausada' : 'Synapse: Sincronização Retomada');
            await this.fetchStatus();
        } else {
            new Notice('Synapse: Falha ao comunicar com o serviço em segundo plano.');
        }
    }

    private async reconnect() {
        new Notice('Synapse: Reconectando ao GitHub...');
        const response = await this.sendIpcCommand('Reconnect');
        if (response && response.payload) {
            new Notice(`Synapse: Conectado (${response.payload.estado})`);
            await this.fetchStatus();
        } else {
            new Notice('Synapse: Não foi possível reconectar.');
        }
    }

    private updateStatusBar(estado: string, pausado: boolean, pendentes: number, ultimaSync?: string | null) {
        if (!this.statusBarItem) return;

        this.statusBarItem.empty();
        const container = this.statusBarItem.createEl('span', { cls: 'synapse-status-bar' });
        container.style.cursor = 'pointer';
        container.onclick = async () => await this.togglePause();

        let iconName = 'check-circle';
        let color = 'var(--text-success)';
        let text = 'Synapse: Sincronizado';

        if (pausado) {
            iconName = 'pause-circle';
            color = 'var(--text-warning)';
            text = 'Synapse: Pausado';
        } else if (estado === 'Sincronizando') {
            iconName = 'refresh-cw';
            color = 'var(--text-accent)';
            text = `Synapse: Sincronizando (${pendentes})`;
        } else if (estado === 'AuthRequired' || estado === 'Erro') {
            iconName = 'alert-triangle';
            color = 'var(--text-error)';
            text = `Synapse: ${estado}`;
        } else if (estado === 'Serviço Offline' || estado === 'Desconectado') {
            iconName = 'cloud-off';
            color = 'var(--text-muted)';
            text = 'Synapse: Offline';
        }

        const iconSpan = container.createEl('span');
        setIcon(iconSpan, iconName);
        iconSpan.style.color = color;
        iconSpan.style.marginRight = '4px';

        const textSpan = container.createEl('span', { text });
        textSpan.style.color = color;

        let tooltip = `Status: ${estado}`;
        if (ultimaSync) {
            tooltip += `\nÚltima sync: ${new Date(ultimaSync).toLocaleTimeString()}`;
        }
        this.statusBarItem.setAttribute('aria-label', tooltip);
    }
}
