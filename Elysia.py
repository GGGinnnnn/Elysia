import sys
import json
import os
import subprocess
import requests
import random
from datetime import datetime
from PyQt6.QtWidgets import (QApplication, QWidget, QLabel, QMenu, 
                             QLineEdit, QVBoxLayout, QHBoxLayout, QStackedWidget,
                             QListWidget, QListWidgetItem, QTextEdit, QPushButton, QFrame, QCheckBox,
                             QInputDialog, QMessageBox, QFileDialog,
                             QRadioButton, QButtonGroup, QSystemTrayIcon)
from PyQt6.QtCore import Qt, QTimer, QThread, pyqtSignal, QPoint
from PyQt6.QtGui import QMovie, QAction, QCursor, QIcon, QContextMenuEvent

os.environ["QT_LOGGING_RULES"] = "qt.gui.imageio.warning=false"

def get_resource_path(relative_path):
    if getattr(sys, 'frozen', False):
        base_path = sys._MEIPASS
    else:
        base_path = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(base_path, relative_path)

def get_executable_dir():
    if getattr(sys, 'frozen', False):
        return os.path.dirname(os.path.abspath(sys.executable))
    else:
        return os.path.dirname(os.path.abspath(__file__))

CONFIG_FILE = os.path.join(get_executable_dir(), "pet_settings.json")

class FetchReplyThread(QThread):
    finished_signal = pyqtSignal(str)
    error_signal = pyqtSignal(str)

    def __init__(self, api_key, preset, prompt):
        super().__init__()
        self.api_key, self.preset, self.prompt = api_key, preset, prompt

    def run(self):
        url = "https://api.deepseek.com/v1/chat/completions"
        headers = {"Authorization": f"Bearer {self.api_key}", "Content-Type": "application/json"}
        data = {
            "model": "deepseek-chat",
            "messages": [
                {"role": "system", "content": self.preset},
                {"role": "user", "content": self.prompt}
            ]
        }
        try:
            response = requests.post(url, json=data, headers=headers, timeout=15, verify=False)
            if response.status_code == 200:
                reply = response.json()['choices'][0]['message']['content']
                self.finished_signal.emit(reply)
            else:
                self.error_signal.emit(f"API错误: {response.status_code}")
        except Exception as e:
            self.error_signal.emit(f"连接失败: {str(e)}")

class MainDashboard(QWidget):
    def __init__(self, pet_instance):
        super().__init__()
        self.pet = pet_instance
        self.setWindowTitle("桌宠管理后台")
        self.setWindowIcon(QIcon(get_resource_path("icon.ico")))
        self.resize(900, 600)
        self.init_ui()

    def init_ui(self):
        main_layout = QHBoxLayout(self)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.setSpacing(0)

        self.sidebar = QFrame()
        self.sidebar.setFixedWidth(200)
        self.sidebar.setStyleSheet("background-color: #2e2e2e; color: white;")
        sidebar_layout = QVBoxLayout(self.sidebar)
        
        self.btn_chat = QPushButton("💬 历史对话")
        self.btn_settings = QPushButton("⚙️ 系统设置")
        self.btn_help = QPushButton("❓ 功能说明")
        self.btn_reminders = QPushButton("⏰ 提醒事项")
        self.btn_chime = QPushButton("🔔 报时设置")
        self.btn_apps = QPushButton("🚀 快捷启动")
        
        for btn in [self.btn_chat, self.btn_settings, self.btn_help, self.btn_reminders, self.btn_chime, self.btn_apps]: 
            btn.setFixedHeight(50)
            btn.setStyleSheet("""
                QPushButton {
                    border: none; text-align: left; padding-left: 20px; font-size: 14px; color: white;
                } 
                QPushButton:hover { background-color: #404040; }
            """)
            sidebar_layout.addWidget(btn)
        sidebar_layout.addStretch()
        
        self.stack = QStackedWidget()

        self.page_chat = QWidget()
        chat_layout = QHBoxLayout(self.page_chat)
        
        self.history_list = QListWidget() 
        self.history_list.setFixedWidth(200)
        self.history_list.setStyleSheet("background-color: #f5f5f5; border:none;")
        self.history_list.itemClicked.connect(self.load_history_detail)
        self.history_list.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        self.history_list.customContextMenuRequested.connect(self.show_history_context_menu)
        
        right_chat_vbox = QVBoxLayout()
        self.chat_display = QTextEdit() 
        self.chat_display.setReadOnly(True)
        self.chat_display.setStyleSheet("border:none; background: white; font-size:14px; padding:10px;")
        
        self.panel_input = QTextEdit() 
        self.panel_input.setFixedHeight(100)
        self.panel_input.setPlaceholderText("在这里输入消息，桌宠也会同步回应...")
        
        btn_send = QPushButton("发送消息")
        btn_send.setFixedSize(100, 30)
        btn_send.clicked.connect(self.send_from_panel)
        
        right_chat_vbox.addWidget(self.chat_display)
        right_chat_vbox.addWidget(self.panel_input)
        right_chat_vbox.addWidget(btn_send, alignment=Qt.AlignmentFlag.AlignRight)
        
        chat_layout.addWidget(self.history_list)
        chat_layout.addLayout(right_chat_vbox)
        self.stack.addWidget(self.page_chat)

        self.page_settings = QWidget()
        settings_layout = QVBoxLayout(self.page_settings)
        settings_layout.setContentsMargins(40, 20, 40, 20)
        
        self.edit_api = QLineEdit(self.pet.config.get("api_key", ""))
        self.edit_preset = QTextEdit(self.pet.config.get("role_preset", ""))
        self.edit_username = QLineEdit(self.pet.config.get("user_info", {}).get("name", "主人"))
        self.edit_idle = QLineEdit(str(int(self.pet.config.get("idle_timeout", 1200)/60)))
        
        self.check_opacity = QCheckBox("开启无操作自动透明化")
        self.check_opacity.setChecked(self.pet.config.get("opacity_enabled", True))
        self.edit_opacity_time = QLineEdit(str(self.pet.config.get("opacity_timeout", 5)))
        self.edit_opacity_time.setPlaceholderText("单位：秒")
        self.edit_opacity_val = QLineEdit(str(self.pet.config.get("opacity_value", 0.3)))
        self.edit_opacity_val.setPlaceholderText("填 0.1 到 1.0（如 0.3 代表 30% 不透明度）")
        
        settings_layout.addWidget(QLabel("显示层级选择 (点击直接切换):"))
        self.level_group = QButtonGroup(self)
        self.radio_top = QRadioButton("所有应用上方 (始终置顶)")
        self.radio_normal = QRadioButton("非全屏应用上方 (全屏应用下自动隐藏)")
        self.radio_bottom = QRadioButton("只显示在桌面 (普通窗口层级)")
        
        self.level_group.addButton(self.radio_top)
        self.level_group.addButton(self.radio_normal)
        self.level_group.addButton(self.radio_bottom)
        
        settings_layout.addWidget(self.radio_top)
        settings_layout.addWidget(self.radio_normal)
        settings_layout.addWidget(self.radio_bottom)
        
        current_level = self.pet.config.get("window_level", "所有应用上方")
        if current_level == "所有应用上方": self.radio_top.setChecked(True)
        elif current_level == "非全屏应用上方": self.radio_normal.setChecked(True)
        elif current_level == "只显示在桌面": self.radio_bottom.setChecked(True)
        
        labels = ["API Key:", "角色预设:", "主人昵称:", "待机提示时长 (分钟):"]
        widgets = [self.edit_api, self.edit_preset, self.edit_username, self.edit_idle]
        for l, w in zip(labels, widgets):
            settings_layout.addWidget(QLabel(l))
            settings_layout.addWidget(w)
            if isinstance(w, QLineEdit): w.setFixedHeight(35)
            
        settings_layout.addWidget(self.check_opacity)
        settings_layout.addWidget(QLabel("无操作变透明时长 (秒):"))
        settings_layout.addWidget(self.edit_opacity_time)
        settings_layout.addWidget(QLabel("透明化程度 (不透明度 0.1~1.0):"))
        settings_layout.addWidget(self.edit_opacity_val)
        
        self.check_tray = QCheckBox("开启系统状态栏(托盘)图标")
        self.check_tray.setChecked(self.pet.config.get("tray_icon_enabled", True))
        settings_layout.addWidget(self.check_tray)
        
        btn_save_all = QPushButton("保存所有设置")
        btn_save_all.setFixedHeight(40)
        btn_save_all.setStyleSheet("background-color: #007bff; color: white; font-weight: bold;")
        btn_save_all.clicked.connect(self.save_all_configs)
        settings_layout.addWidget(btn_save_all)
        settings_layout.addStretch()
        self.stack.addWidget(self.page_settings)

        self.page_help = QWidget()
        help_layout = QVBoxLayout(self.page_help)
        help_layout.setContentsMargins(40, 20, 40, 20)
        help_title = QLabel("🤖 桌宠使用与功能说明")
        help_title.setStyleSheet("font-size: 18px; font-weight: bold; color: #333;")
        self.help_content = QTextEdit()
        self.help_content.setReadOnly(True)
        self.help_content.setStyleSheet("border: 1px solid #ccc; background: #fdfdfd; font-size: 14px; padding: 15px; line-height: 1.6;")
        self.help_content.setHtml("""
            <h3>🌸 欢迎来到爱莉希雅的秘密花园 ♪</h3>
            <p>美丽、自信又热情的妖精小姐已经入驻你的桌面啦！她不仅能为你带来温馨的陪伴，更是你无所不能的超级桌面管家。快来看看她都有哪些厉害的本领吧~</p>
            <hr/>
            <h3>✨ 一、 视觉与丝滑交互</h3>
            <ul>
                <li><b>快捷对话框：</b> 鼠标左键点击桌宠，即可唤醒或隐藏底部居中的精致输入框。</li>
                <li><b>智能隐形：</b> 输入框展开后若 10 秒内无操作，会自动静默缩回，绝不占用你的宝贵屏幕空间♪</li>
                <li><b>自由等比例缩放：</b> 鼠标右键按住桌宠身体并横向拖动，桌宠、聊天气泡、输入框及字体都会<b>完美等比例同步放大或缩小</b>，大小随心所欲~</li>
                <li><b>无痕桌面拖拽：</b> 左键按住桌宠身体，可在屏幕任意位置自由拖动摆放。</li>
            </ul>
            <h3>💬 二、 智能对话与情绪联动</h3>
            <ul>
                <li><b>完美人设沉浸：</b> 接入大模型 AI 后，爱莉希雅将以最自然、欢快的语气与你畅聊。</li>
                <li><b>丰富动态表情：</b> 触发特定对话时，她会根据聊天语境实时切换眨眼、开心、思考、哭泣、疑问、傲娇等 8 种精美动图状态！</li>
                <li><b>历史对话管理：</b> 后台支持查看完整的聊天记录。如果有什么悄悄话不想被别人看到，在左侧记录列表<b>点击右键即可精准删除单条历史</b>。</li>
            </ul>
            <h3>⏰ 三、 专属时钟与智能日程管家</h3>
            <ul>
                <li><b>爱莉希雅式整点报时：</b> 开启报时后，每到整点她都会为你送上专属的时间关怀。每个时段支持配置多句不同的精致语录，<b>每次报时随机抽取</b>，充满新鲜感~</li>
                <li><b>声控智能日程记录：</b> 无需繁琐操作，直接在聊天输入框对她说 <i>“12:00提醒我吃饭”</i> 或 <i>“15:30提醒我开会”</i>，她就能瞬间听懂并记录在小本本上♪</li>
                <li><b>强力双重提醒：</b> 设定时间一到，桌宠不仅会冒出气泡提示，更会<b>在屏幕中央弹出强力系统对话框</b>，双重保险，重要事情绝不漏掉！</li>
            </ul>
            <h3>🚀 四、 生产力快捷指令</h3>
            <p>在快捷输入框中输入 <b>“斜杠 / + 中文关键词”</b> 并回车，爱莉希雅就能像魔法一样帮你秒开各种常用的 Windows 系统工具：</p>
            <ul>
                <li>输入 <b>/终端</b> ➔ 唤醒黑色命令行窗口 (CMD)</li>
                <li>输入 <b>/任务管理器</b> ➔ 快捷查看或强制结束卡死进程</li>
                <li>输入 <b>/设备管理器</b> ➔ 快速查看电脑硬件与驱动</li>
                <li>输入 <b>/服务</b> ➔ 一键直达 Windows 本地服务设置</li>
                <li>输入 <b>/控制面板</b> ➔ 开启传统系统设置中心</li>
                <li>输入 <b>/计算器</b> ➔ 弹出系统自带计算器</li>
                <li>输入 <b>/记事本</b> ➔ 秒开临时草稿本</li>
                <li>输入 <b>/网卡设置</b> ➔ 快速查看或修复网络连接</li>
            </ul>
            <h3>💻 五、 右键菜单自定义快捷启动</h3>
            <ul>
                <li><b>软件快捷绑定：</b> 在后台的「快捷启动」栏目中，你可以自由添加常用软件的名字（如：Steam、网易云、浏览器），并选取它们的物理路径。</li>
                <li><b>动态菜单生成：</b> 设置完成后，<b>桌宠的右键菜单顶部会实时吐出你添加的软件选项</b>！无需去桌面翻找，右键点击爱莉希雅即可一键秒开你最爱的游戏或应用，看板娘属性直接拉满♪</li>
            </ul>
            <hr/>
            <p align="right"><i>“大好的时光，有爱莉希雅陪着你，每分每秒都很特别哦~ ♪”</i></p>
        """)
        help_layout.addWidget(help_title)
        help_layout.addWidget(self.help_content)
        self.stack.addWidget(self.page_help)

        self.page_reminders = QWidget()
        rem_layout = QVBoxLayout(self.page_reminders)
        self.reminder_list = QListWidget() 
        self.btn_add_rem = QPushButton("➕ 手动添加新日程")
        self.btn_del_rem = QPushButton("❌ 删除选中日程")
        self.btn_add_rem.clicked.connect(self.add_reminder_dialog)
        self.btn_del_rem.clicked.connect(self.delete_reminder)
        rem_layout.addWidget(QLabel("📅 当前日程提醒列表："))
        rem_layout.addWidget(self.reminder_list)
        btn_h_box = QHBoxLayout()
        btn_h_box.addWidget(self.btn_add_rem)
        btn_h_box.addWidget(self.btn_del_rem)
        rem_layout.addLayout(btn_h_box)
        self.stack.addWidget(self.page_reminders)

        self.page_chime = QWidget()
        chime_layout = QVBoxLayout(self.page_chime)
        self.check_chime = QCheckBox("开启整点报时")
        self.check_chime.setChecked(self.pet.config.get("hourly_chime", True))
        self.check_chime.stateChanged.connect(self.save_chime_state)
        self.chime_edit = QTextEdit() 
        btn_save_chime = QPushButton("💾 保存自定义报时语")
        btn_save_chime.setFixedHeight(35)
        btn_save_chime.clicked.connect(self.save_chime_messages)
        chime_layout.addWidget(self.check_chime)
        chime_layout.addWidget(QLabel("📝 自定义报时语录 (每行一条，修改后请点击保存)："))
        chime_layout.addWidget(self.chime_edit)
        chime_layout.addWidget(btn_save_chime)
        self.stack.addWidget(self.page_chime)
        
        self.page_apps = QWidget()
        apps_layout = QVBoxLayout(self.page_apps)
        apps_layout.setContentsMargins(30, 20, 30, 20)
        self.apps_list = QListWidget()
        self.btn_add_app = QPushButton("➕ 添加快捷启动程序")
        self.btn_del_app = QPushButton("❌ 删除选中程序")
        self.btn_add_app.clicked.connect(self.add_app_dialog)
        self.btn_del_app.clicked.connect(self.delete_app)
        apps_layout.addWidget(QLabel("🚀 快捷启动软件列表（设置后将实时同步到桌宠右键菜单）："))
        apps_layout.addWidget(self.apps_list)
        apps_btn_box = QHBoxLayout()
        apps_btn_box.addWidget(self.btn_add_app)
        apps_btn_box.addWidget(self.btn_del_app)
        apps_layout.addLayout(apps_btn_box)
        self.stack.addWidget(self.page_apps)
        
        main_layout.addWidget(self.sidebar)
        main_layout.addWidget(self.stack)

        self.btn_chat.clicked.connect(lambda: self.stack.setCurrentIndex(0))
        self.btn_settings.clicked.connect(lambda: self.stack.setCurrentIndex(1))
        self.btn_help.clicked.connect(lambda: self.stack.setCurrentIndex(2))
        self.btn_reminders.clicked.connect(lambda: [self.stack.setCurrentIndex(3), self.refresh_reminders()])
        self.btn_chime.clicked.connect(lambda: [self.stack.setCurrentIndex(4), self.load_chime_to_ui()])
        self.btn_apps.clicked.connect(lambda: [self.stack.setCurrentIndex(5), self.refresh_apps()])

    def closeEvent(self, event):
        event.ignore()  
        self.hide()     

    def refresh_history(self):
        self.history_list.clear()
        for i, item in enumerate(reversed(self.pet.config.get("history", []))):
            title = item["user"][:12] + ("..." if len(item["user"]) > 12 else "")
            actual_index = len(self.pet.config['history']) - 1 - i
            list_item = QListWidgetItem(f"对谈 {actual_index + 1}\n{title}")
            list_item.setData(Qt.ItemDataRole.UserRole, actual_index)
            self.history_list.addItem(list_item)

    def show_history_context_menu(self, pos):
        item = self.history_list.itemAt(pos)
        if item is None: return
        menu = QMenu(self)
        delete_action = QAction("❌ 删除此条对话记录", self)
        actual_index = item.data(Qt.ItemDataRole.UserRole)
        delete_action.triggered.connect(lambda: self.delete_history_item(actual_index))
        menu.addAction(delete_action)
        menu.exec(QCursor.pos())

    def delete_history_item(self, index):
        if 0 <= index < len(self.pet.config.get("history", [])):
            self.pet.config["history"].pop(index)
            self.pet.save_config()
            self.chat_display.clear() 
            self.refresh_history()    

    def load_history_detail(self, item):
        actual_index = item.data(Qt.ItemDataRole.UserRole)
        data = self.pet.config["history"][actual_index]
        self.chat_display.setPlainText(f"主人: {data['user']}\n\n桌宠: {data['pet']}")

    def send_from_panel(self):
        text = self.panel_input.toPlainText().strip()
        if text:
            self.pet.call_api(text)
            self.panel_input.clear()
            self.chat_display.append(f"\n主人: {text}\n(正在等待桌宠回应...)")

    def refresh_reminders(self):
        self.reminder_list.clear()
        for i, rem in enumerate(self.pet.config.get("reminders", [])):
            self.reminder_list.addItem(f"⏰ [{rem['time']}] {rem['content']}")

    def add_reminder_dialog(self):
        time_str, ok1 = QInputDialog.getText(self, "添加日程", "请输入时间 (格式 如 14:30):")
        if not ok1 or not time_str: return
        content, ok2 = QInputDialog.getText(self, "添加日程", "请输入提醒内容:")
        if not ok2 or not content: return
        
        self.pet.config["reminders"].append({"time": time_str.strip(), "content": content.strip()})
        self.pet.save_config()
        self.refresh_reminders()

    def delete_reminder(self):
        current_row = self.reminder_list.currentRow()
        if current_row >= 0:
            self.pet.config["reminders"].pop(current_row)
            self.pet.save_config()
            self.refresh_reminders()

    def save_chime_state(self, state):
        self.pet.config["hourly_chime"] = self.check_chime.isChecked()
        self.pet.save_config()

    def load_chime_to_ui(self):
        text_lines = []
        messages = self.pet.config.get("hourly_messages", {})
        for hour, msgs in messages.items():
            for msg in msgs:
                text_lines.append(f"{hour}|{msg}")
        self.chime_edit.setPlainText("\n".join(text_lines))

    def save_chime_messages(self):
        raw_text = self.chime_edit.toPlainText().strip()
        new_messages = {}
        for line in raw_text.split("\n"):
            if "|" in line:
                parts = line.split("|", 1)
                hour = parts[0].strip().zfill(2)
                msg = parts[1].strip()
                if hour not in new_messages:
                    new_messages[hour] = []
                new_messages[hour].append(msg)
        self.pet.config["hourly_messages"] = new_messages
        self.pet.save_config()
        QMessageBox.information(self, "提示", "✨ 自定义报时语录保存成功！")

    def save_all_configs(self):
        self.pet.config["api_key"] = self.edit_api.text().strip()
        self.pet.config["role_preset"] = self.edit_preset.toPlainText().strip()
        self.pet.config["tray_icon_enabled"] = self.check_tray.isChecked()
        if "user_info" not in self.pet.config: self.pet.config["user_info"] = {}
        self.pet.config["user_info"]["name"] = self.edit_username.text().strip()
        
        try: self.pet.config["idle_timeout"] = int(self.edit_idle.text()) * 60
        except: pass
        
        self.pet.config["opacity_enabled"] = self.check_opacity.isChecked()
        try: self.pet.config["opacity_timeout"] = int(self.edit_opacity_time.text())
        except: pass
        try: self.pet.config["opacity_value"] = float(self.edit_opacity_val.text())
        except: pass
        
        if self.radio_top.isChecked():
            self.pet.config["window_level"] = "所有应用上方"
        elif self.radio_normal.isChecked():
            self.pet.config["window_level"] = "非全屏应用上方"
        elif self.radio_bottom.isChecked():
            self.pet.config["window_level"] = "只显示在桌面"
            
        self.pet.save_config()
        
        self.pet.apply_window_level()
        self.pet.reset_opacity()
        self.pet.update_tray_icon_status()
        
        self.pet.show_message("✨ 设置已同步！")

    def refresh_apps(self):
        self.apps_list.clear()
        for app in self.pet.config.get("quick_apps", []):
            self.apps_list.addItem(f"📂 {app['name']} ➔ {app['path']}")

    def add_app_dialog(self):
        name, ok = QInputDialog.getText(self, "添加快捷启动", "请输入显示在右键菜单的名称:")
        if not ok or not name.strip(): return
        
        file_path, _ = QFileDialog.getOpenFileName(self, "选取执行程序", "", "应用程序 (*.exe);;所有文件 (*)")
        if not file_path: return
        
        if "quick_apps" not in self.pet.config:
            self.pet.config["quick_apps"] = []
            
        self.pet.config["quick_apps"].append({"name": name.strip(), "path": file_path})
        self.pet.save_config()
        self.refresh_apps()
        self.pet.show_message(f"成功添加快捷应用：{name} ♪")

    def delete_app(self):
        current_row = self.apps_list.currentRow()
        if current_row >= 0:
            self.pet.config["quick_apps"].pop(current_row)
            self.pet.save_config()
            self.refresh_apps()

class DesktopPet(QWidget):
    def __init__(self):
        super().__init__()
        self.m_drag = False       
        self.m_resize = False     
        self.is_typing = False    
        self.last_click_time = 0  
        self.orig_width, self.orig_height = 220, 220 
        self.last_hour = -1       
        
        self.load_config()                 
        self.dashboard = MainDashboard(self) 
        self.init_ui()                     
        self.init_timers()                 
        self.tray_icon = None              
        self.update_tray_icon_status()     
        self.setWindowIcon(QIcon(get_resource_path("icon.ico")))

    def update_tray_icon_status(self):
        enabled = self.config.get("tray_icon_enabled", True)
        
        if enabled:
            if not self.tray_icon:
                self.tray_icon = QSystemTrayIcon(self)
                self.tray_icon.setIcon(QIcon(get_resource_path("icon.ico")))
                self.tray_icon.setToolTip("爱莉希雅桌宠 ♪")
                
                tray_menu = QMenu()
                tray_menu.setStyleSheet("QMenu { background: white; border: 1px solid #ccc; }")
                
                a0 = QAction("💬 历史对话面板", self)
                a0.triggered.connect(lambda: self.open_dashboard(0))
                a1 = QAction("⚙️ 设定修改", self)
                a1.triggered.connect(lambda: self.open_dashboard(1))
                a3 = QAction("⏰ 提醒事项", self)
                a3.triggered.connect(lambda: self.open_dashboard(3))
                a4 = QAction("🔔 报时设置", self)
                a4.triggered.connect(lambda: self.open_dashboard(4))
                
                a_reset = QAction("🔄 重置位置与强制置顶", self)
                a_reset.triggered.connect(self.reset_pet_position)
                
                a_exit = QAction("❌ 退出应用", self)
                a_exit.triggered.connect(lambda: sys.exit(0))
                
                tray_menu.addAction(a_reset)
                tray_menu.addSeparator()
                tray_menu.addAction(a0)
                tray_menu.addAction(a1)
                tray_menu.addAction(a3)
                tray_menu.addAction(a4)
                tray_menu.addSeparator()
                tray_menu.addAction(a_exit)
                
                self.tray_icon.setContextMenu(tray_menu)
                self.tray_icon.show()
        else:
            if self.tray_icon:
                self.tray_icon.hide()
                self.tray_icon = None

    def reset_pet_position(self):
        self.move(1400, 400) 
        self.apply_window_level() 
        self.reset_opacity()      
        self.show()
        self.raise_()
        self.activateWindow()     
        self.show_message("妖精小姐闪现回来啦！没有让你久等吧~ ♪")
        self.change_state("wink")

    def load_config(self):
        default_config = {
            "api_key": "", 
            "role_preset": "接下来你将扮演崩坏3里的爱莉希雅，对话时要自然，回答要符合人设。回答应情景式、对话式。不要强调自己的身份。回答允许休闲。回答避免总结。回答不应抽象、详细解释、追溯原因。回答不要使用emoji。不要使用换行符。回答不允许使用md语法。对于无意义的重复某句话之类的不予理会,回答不要包括思考过程，直接给出回答，不要发送<think>和</think>这类字符，不要使用*和#这类特殊字符，不要描写动作。要像游戏里一样自然，不要用括号写出人物的动作，学习游戏中的语句,请根据你说话的语气和情感，在每句回答的【最后面】加上且只加上一个以下规定格式的心情标签，不要有多余的字。标签列表：[waiting]、[cry]、[question]、[wink]、[like]、[speechless]、[hurry]。例如：主人今天真帅！[like]", 
            "user_info": {"name": "主人"}, 
            "history": [], 
            "idle_timeout": 1200,
            "hourly_chime": True,
            "opacity_enabled": True,       
            "opacity_timeout": 5,          
            "opacity_value": 0.3,          
            "window_level": "所有应用上方", 
            "tray_icon_enabled": True,    
            "hourly_messages": {
                "00": ["哎呀，已经是午夜零点了呢~ 璀璨的星空下，是不是该和可爱的爱莉希雅说晚安了呢？", "零点啦！妖精小姐的魔法时间到~ 还不睡的话，我可要在你的梦里捣乱了哦~"],
                "01": ["一点整啦，夜深人静的时候，最适合回忆那些美好的邂逅了，对不对~"],
                "02": ["两点整。悄悄看看，是谁还在修仙呀？哪怕是舰长，不好好休息我也会生气的哦~"],
                "06": ["清晨六点整！呼哈~ 太阳升起啦，新的一天，也要充满对爱莉希雅的期待哦~"],
                "07": ["七点整~ 睁开眼第一个想到的，会不会是我呢？早安，亲爱的~"],
                "08": ["八点整！美好的早晨，需要一杯热牛奶，还有……一个来自爱莉希雅的甜美微笑~"],
                "12": ["叮咚！中午十二点整~ 到了最期待的午餐时间啦！今天想和爱莉希雅一起吃点什么呢？"],
                "13": ["一点整，午后的阳光懒洋洋的~ 稍微眯一会儿吧，我会一直守在你的桌面上哦~"],
                "18": ["傍晚六点整~ 忙碌的工作/学业辛苦啦！快伸个懒腰，接下来是属于我们的时间了呢~"],
                "21": ["九点整啦。累了一天，快坐下来喝杯热茶，听爱莉希雅给你讲故事吧~"],
                "22": ["十点整。夜色渐浓，妖精小姐的魅力是不是也加倍了呢？~"],
                "23": ["二十三点整。快去洗漱准备睡觉啦，熬夜可是美貌的大敌，虽然爱莉希雅永远完美就是了~"]
            },
            "reminders": [] 
        }
        
        if os.path.exists(CONFIG_FILE):
            try:
                with open(CONFIG_FILE, 'r', encoding='utf-8') as f: 
                    self.config = json.load(f)

                if "quick_apps" not in self.config:
                    self.config["quick_apps"] = []
                
                for key, value in default_config.items():
                    if key not in self.config:
                        self.config[key] = value
                return 
                
            except Exception as e:
                print(f"读取配置失败，将重建配置。原因: {e}")
                self.config = default_config
        else:
            self.config = default_config
            
        self.save_config()

    def check_time_events(self):
        now = datetime.now()
        current_time = now.strftime("%H:%M")
        
        if self.config.get("hourly_chime", True) and now.minute == 0 and now.second == 0:
            if now.hour != self.last_hour:
                self.last_hour = now.hour
                hour_str = str(now.hour).zfill(2)
                msgs = self.config.get("hourly_messages", {}).get(hour_str, [f"铛铛！已经{now.hour}点整了哦~"])
                msg = random.choice(msgs)
                self.show_message(msg)
                self.change_state("wink")

        for rem in self.config.get("reminders", []):
            if rem["time"] == current_time and now.second == 0:
                self.show_message(f"⏰ 亲爱的！妖精小姐来提醒你：\n『 {rem['content']} 』")
                self.change_state("hurry")
                QTimer.singleShot(100, lambda r=rem: QMessageBox.information(None, "⏰ 日程提醒", f"爱莉希雅提醒你：\n\n{r['content']}"))

    def save_config(self):
        try:
            os.makedirs(os.path.dirname(CONFIG_FILE), exist_ok=True)
            with open(CONFIG_FILE, 'w', encoding='utf-8') as f: 
                json.dump(self.config, f, ensure_ascii=False, indent=4)
        except Exception as e:
            print(f"写入配置文件失败: {e}")

    def init_ui(self):
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        
        self.bubble = QLabel(self)
        self.bubble.setWordWrap(True)  
        self.bubble.setStyleSheet("""
            background-color: rgba(255, 255, 255, 0.95); border: 3px solid #FFB6C1; 
            border-radius: 16px; padding: 16px; font-family: 'Microsoft YaHei'; font-size: 16px; color: #333333;
        """)
        self.bubble.hide()

        self.pet_label = QLabel(self)
        self.pet_label.setScaledContents(True) 
        init_path = get_resource_path("assets/waiting.gif")
        if os.path.exists(init_path):
            self.movie = QMovie(init_path)
            self.pet_label.setMovie(self.movie)
            self.movie.start()
        else:
            self.pet_label.setText("【素材丢失】")

        self.input_box = QLineEdit(self)
        self.input_box.setPlaceholderText("聊点什么？或输入'12:00提醒我吃饭'")
        self.input_box.hide()

        self.resize(self.orig_width, self.orig_height) 
        self.update_window_total_size()                
        self.move(1400, 400)                           
        self.apply_window_level()                      

    def init_timers(self):
        self.state_timer = QTimer(self)
        self.state_timer.timeout.connect(self.check_time_state)
        self.state_timer.start(60000)

        self.time_check_timer = QTimer(self)
        self.time_check_timer.timeout.connect(self.check_time_events)
        self.time_check_timer.start(1000) 

        self.idle_count = 0
        self.idle_timer = QTimer(self)
        self.idle_timer.timeout.connect(self.check_idle)
        self.idle_timer.start(1000)

        self.fade_timer = QTimer(self)
        self.fade_timer.setSingleShot(True)
        self.fade_timer.timeout.connect(self.fade_out_bubble)

        self.input_hide_timer = QTimer(self)
        self.input_hide_timer.setSingleShot(True)
        self.input_hide_timer.timeout.connect(self.auto_hide_input_box)

        self.opacity_timer = QTimer(self)
        self.opacity_timer.timeout.connect(self.check_opacity_timeout)
        self.opacity_timer.start(1000) 
        self.user_active_count = 0     

    def apply_window_level(self):
        level = self.config.get("window_level", "所有应用上方")
        base_flags = Qt.WindowType.FramelessWindowHint | Qt.WindowType.Tool
        
        if level == "所有应用上方":
            self.setWindowFlags(base_flags | Qt.WindowType.WindowStaysOnTopHint)
        elif level == "非全屏应用上方":
            self.setWindowFlags(base_flags | Qt.WindowType.WindowStaysOnTopHint)
        elif level == "只显示在桌面":
            self.setWindowFlags(base_flags | Qt.WindowType.WindowStaysOnBottomHint)
            
        self.show() 

    def check_opacity_timeout(self):
        if not self.config.get("opacity_enabled", True): return
            
        if self.underMouse() or (self.input_box.isVisible() and self.input_box.hasFocus()):
            self.reset_opacity()
            return
            
        self.user_active_count += 1
        
        if self.user_active_count >= self.config.get("opacity_timeout", 5):
            target_opacity = self.config.get("opacity_value", 0.3)
            target_opacity = max(0.1, min(1.0, target_opacity)) 
            self.setWindowOpacity(target_opacity)

    def reset_opacity(self):
        self.user_active_count = 0
        self.setWindowOpacity(1.0)

    def enterEvent(self, event):
        self.reset_opacity()
        super().enterEvent(event)

    def leaveEvent(self, event):
        self.user_active_count = 0
        super().leaveEvent(event)

    def check_time_state(self):
        hour = datetime.now().hour
        if hour >= 22 or hour < 8: self.change_state("sleep")

    def check_idle(self):
        self.idle_count += 1
        if self.idle_count >= self.config.get("idle_timeout", 1200):
            self.show_message("主人，你还在忙吗？默默陪着你...")
            self.idle_count = 0
            self.change_state("hurry")

    def auto_hide_input_box(self):
        if self.input_box.isVisible():
            self.input_box.clear()
            self.input_box.hide()
            self.update_window_total_size()

    def update_window_total_size(self):
        current_width = self.width() 
        scale_ratio = self.orig_height / self.orig_width 
        pet_height = int(current_width * scale_ratio)     
        scale_factor = current_width / self.orig_width    

        bubble_height = 0
        if self.bubble.isVisible():
            self.bubble.setMaximumWidth(int(current_width * 2)) 
            bubble_height = self.bubble.sizeHint().height()
            self.bubble.setGeometry(0, 0, current_width, bubble_height)
        
        self.pet_label.setGeometry(0, bubble_height, current_width, pet_height)
        
        input_height = 0
        if self.input_box.isVisible():
            new_input_width = int(200 * scale_factor)                        
            new_input_height = max(20, int(30 * scale_factor))               
            new_input_font = max(10, int(13 * scale_factor))                
            
            self.input_box.setFixedSize(new_input_width, new_input_height)
            self.input_box.setStyleSheet(f"""
                font-size: {new_input_font}px; height: {new_input_height}px; padding: 4px; 
                border: 2px solid #FFB6C1; border-radius: {max(4, int(6*scale_factor))}px; background: white;
            """)
            
            input_height = self.input_box.height()
            input_width = self.input_box.width()
            input_x = (current_width - input_width) // 2
            self.input_box.setGeometry(input_x, bubble_height + pet_height + 10, input_width, input_height)
            
        total_height = bubble_height + pet_height + (input_height + 20 if input_height > 0 else 0)
        self.setFixedSize(current_width, total_height)

    def mousePressEvent(self, event):
        self.reset_opacity() 
        current_time = datetime.now().timestamp()
        
        if self.input_box.geometry().contains(event.pos()) and not self.input_box.isHidden():
            self.input_hide_timer.start(10000)
            event.accept()
            return

        if self.bubble.isVisible():
            if self.is_typing:
                self.typewriter_timer.stop()
                self.bubble.setText(self.full_text)
                self.is_typing = False
                self.fade_timer.start(3000)
                event.accept()
                return
            elif current_time - self.last_click_time > 0.5:
                self.fade_timer.stop()
                self.fade_out_bubble()
                event.accept()
                return

        if event.button() == Qt.MouseButton.LeftButton:
            self.m_drag = True
            self.m_DragPosition = event.globalPosition().toPoint() - self.pos()
            
            if self.input_box.isHidden():
                self.input_box.show()
                self.input_box.setFocus()
                self.input_hide_timer.start(10000)
            else:
                if not self.input_box.text().strip():
                    self.input_box.hide()
                    self.input_hide_timer.stop()
            
            self.update_window_total_size() 
            event.accept()

        elif event.button() == Qt.MouseButton.RightButton:
            self.m_resize = True
            self.start_pos = event.globalPosition().toPoint() 
            self.start_width, self.start_height = self.width(), self.height()                 
            event.accept()

    def mouseMoveEvent(self, event):
        if Qt.MouseButton.LeftButton and self.m_drag:
            self.move(event.globalPosition().toPoint() - self.m_DragPosition)
            event.accept()
            
        elif Qt.MouseButton.RightButton and self.m_resize:
            current_pos = event.globalPosition().toPoint()
            delta_x = current_pos.x() - self.start_pos.x() 
            
            new_width = self.start_width + delta_x
            if new_width < 120: new_width = 120 
            if new_width > 800: new_width = 800 
            
            self.setFixedSize(new_width, self.height())
            self.update_window_total_size() 
            event.accept()
    
    def mouseReleaseEvent(self, event):
        self.m_drag = False
        if event.button() == Qt.MouseButton.RightButton and self.m_resize:
            self.m_resize = False
            distance = (event.globalPosition().toPoint() - self.start_pos).manhattanLength()
            if distance < 5: self.popup_menu(event)
            event.accept()

    def keyPressEvent(self, event):
        if event.key() in (Qt.Key.Key_Return, Qt.Key.Key_Enter):
            if self.input_box.isVisible() and self.input_box.hasFocus():
                self.input_box.hide()
                self.input_hide_timer.stop() 
                self.handle_input()
                event.accept()
                return
        super().keyPressEvent(event)

    def popup_menu(self, event):
        menu = QMenu(self)
        menu.setStyleSheet("QMenu { background: white; border: 1px solid #ccc; padding: 5px; }")
        
        quick_apps = self.config.get("quick_apps", [])
        if quick_apps:
            for app in quick_apps:
                app_action = QAction(f"🚀 {app['name']}", self)
                app_action.triggered.connect(lambda chk, p=app['path']: self.launch_exe(p))
                menu.addAction(app_action)
            menu.addSeparator() 

        actions = [
            ("历史对话面板", 0), ("设定修改", 1), ("提醒事项", 3), ("报时设置", 4), ("快捷启动设置", 5)
        ]
        for name, index in actions:
            action = QAction(name, self)
            action.triggered.connect(lambda chk, idx=index: self.open_dashboard(idx))
            menu.addAction(action)
        
        menu.addSeparator()
        a_reset = QAction("🔄 重置位置与置顶", self)
        a_reset.triggered.connect(self.reset_pet_position)
        menu.addAction(a_reset)
        
        menu.addSeparator()
        q_action = QAction("退出应用", self)
        q_action.triggered.connect(lambda: sys.exit(0))
        menu.addAction(q_action)
        menu.exec(QCursor.pos())
    
    def launch_exe(self, exe_path):
        if os.path.exists(exe_path):
            try:
                subprocess.Popen(exe_path, shell=True)
                self.show_message("已经为你开启程序啦，指挥官~ ♪")
                self.change_state("wink")
            except Exception as e:
                self.show_message(f"唔……启动失败了呢，原因: {e}")
                self.change_state("cry")
        else:
            self.show_message("妖精小姐找不到这个文件了，路径是不是被移动了呀？")
            self.change_state("question")

    def open_dashboard(self, tab_index):
        self.dashboard.stack.setCurrentIndex(tab_index)
        if tab_index == 0: self.dashboard.refresh_history()
        elif tab_index == 3: self.dashboard.refresh_reminders()
        elif tab_index == 4: self.dashboard.load_chime_to_ui()
        elif tab_index == 5: self.dashboard.refresh_apps()
        self.dashboard.show()
        self.dashboard.raise_()

    def change_state(self, state):
        paths = {"sleep": "assets/sleep.gif", "waiting": "assets/waiting.gif", "cry": "assets/cry.gif", "question": "assets/question.gif", "wink": "assets/wink.gif", "like": "assets/like.gif", "speechless": "assets/speechless.gif", "hurry": "assets/hurry.gif"}
        target_path = get_resource_path(paths.get(state, "assets/waiting.gif"))
        if os.path.exists(target_path):
            self.movie.stop()
            self.movie.setFileName(target_path)
            self.movie.start()

    def handle_input(self):
        self.reset_opacity()
        text = self.input_box.text().strip()
        if not text: 
            self.update_window_total_size()
            return
        self.input_box.clear()

        import re
        time_match = re.search(r'(\d{1,2}[:：]\d{2})', text)
        if time_match and "提醒" in text:
            rem_time = time_match.group(1).replace("：", ":") 
            content = text.split("提醒我")[-1].strip()
            self.config["reminders"].append({"time": rem_time, "content": content})
            self.save_config()
            self.show_message(f"已经记在心里啦，{rem_time} 准时叫你哦~ ♪")
            self.change_state("wink")
            return
        
        if text.startswith("/"):
            tools = {
                "cmd": "cmd", "命令行": "cmd", "终端": "cmd",
                "计算器": "calc", "记事本": "notepad", "画图": "mspaint",
                "控制面板": "control", "设备管理器": "devmgmt.msc", "服务": "services.msc",
                "任务管理器": "taskmgr", "注册表": "regedit", "磁盘清理": "cleanmgr",
                "计算机管理": "compmgmt.msc", "事件": "eventvwr.msc", "配置诊断": "dxdiag",
                "网络": "ncpa.cpl", "网卡设置": "ncpa.cpl", "防火墙": "control firewall.cpl"
            }
            cmd_key = text[1:].strip().lower() 
            
            if cmd_key in tools:
                try:
                    subprocess.Popen(tools[cmd_key], shell=True)
                    self.show_message(f"已经为你唤醒『{cmd_key}』啦，指挥官~ ♪")
                    self.change_state("wink")
                except Exception as e:
                    self.show_message(f"唔……唤醒失败了，可能系统不兼容呢: {e}")
                    self.change_state("cry")
            else:
                self.show_message(f"爱莉希雅没听过『/{cmd_key}』这个指令呢，要检查一下吗？")
                self.change_state("question")
            self.update_window_total_size()
            
        else:
            self.call_api(text)

    def call_api(self, prompt):
        api_key = self.config.get("api_key", "")
        if not api_key:
            self.show_message("请先绑定你的 API Key。")
            return
        self.show_message("思考中...")
        self.change_state("question") 
        
        self.api_thread = FetchReplyThread(api_key, self.config["role_preset"], prompt)
        self.api_thread.finished_signal.connect(lambda reply: self.on_api_success(prompt, reply))
        self.api_thread.error_signal.connect(lambda err: self.show_message(err))
        self.api_thread.start()

    def on_api_success(self, prompt, reply):
        state = "wink"
        import re
        match = re.search(r'\[(waiting|cry|question|wink|like|speechless|hurry)\]', reply)
        if match:
            state = match.group(1) 
            reply = re.sub(r'\[(waiting|cry|question|wink|like|speechless|hurry)\]', '', reply).strip()
        
        self.change_state(state) 
        self.show_message(reply) 
        
        self.dashboard.chat_display.append(f"\n主人: {prompt}\n桌宠: {reply}\n" + "-"*30)
        self.config["history"].append({"user": prompt, "pet": reply})
        self.save_config()
        self.dashboard.refresh_history()

    def show_message(self, text):
        self.fade_timer.stop()
        self.bubble.show()
        self.full_text = text
        self.current_idx = 0
        self.is_typing = True
        
        if hasattr(self, 'typewriter_timer') and self.typewriter_timer.isActive():
            self.typewriter_timer.stop()
        self.typewriter_timer = QTimer(self)
        self.typewriter_timer.timeout.connect(self.update_typewriter)
        self.typewriter_timer.start(40) 

    def update_typewriter(self):
        if self.current_idx <= len(self.full_text):
            self.bubble.setText(self.full_text[:self.current_idx])
            self.current_idx += 1
            self.update_window_total_size() 
        else:
            self.typewriter_timer.stop()
            self.is_typing = False
            self.last_click_time = datetime.now().timestamp()
            self.fade_timer.start(3000) 

    def fade_out_bubble(self):
        self.bubble.hide()
        self.update_window_total_size() 
        hour = datetime.now().hour
        if hour >= 22 or hour < 8: self.change_state("sleep")
        else: self.change_state("waiting") 

if __name__ == '__main__':
    app = QApplication(sys.argv)
    pet = DesktopPet()
    pet.show() 
    sys.exit(app.exec())