import sys
import json
import os
import subprocess
import requests
import random
from datetime import datetime
# 导入 PyQt6 的界面核心组件：包含应用、窗口、标签、右键菜单、输入框、各种布局、列表、单选按钮和托盘图标
from PyQt6.QtWidgets import (QApplication, QWidget, QLabel, QMenu, 
                             QLineEdit, QVBoxLayout, QHBoxLayout, QStackedWidget,
                             QListWidget, QListWidgetItem, QTextEdit, QPushButton, QFrame, QCheckBox,
                             QInputDialog, QMessageBox, QFileDialog,
                             QRadioButton, QButtonGroup, QSystemTrayIcon)
# 导入 PyQt6 的核心控制属性、定时器、子线程、信号槽和点坐标
from PyQt6.QtCore import Qt, QTimer, QThread, pyqtSignal, QPoint
# 导入 PyQt6 的多媒体动画（GIF支持）、动作监听、鼠标指针控制、图标和右键菜单事件
from PyQt6.QtGui import QMovie, QAction, QCursor, QIcon, QContextMenuEvent

# 屏蔽 Qt 内部频繁弹出的不合规 PNG 图片格式的警告，保持控制台干净
os.environ["QT_LOGGING_RULES"] = "qt.gui.imageio.warning=false"

def get_resource_path(relative_path):
    """ 获取素材文件的绝对路径（完美兼容本地源码运行和 PyInstaller 打包后的临时目录） """
    if getattr(sys, 'frozen', False):
        # 如果是打包后的环境，PyInstaller 会把素材解压到 sys._MEIPASS 临时路径中
        base_path = sys._MEIPASS
    else:
        # 如果是本地开发环境，直接获取当前 main.py 文件的同级目录
        base_path = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(base_path, relative_path)

def get_executable_dir():
    """ 获取程序真正运行的物理目录（打包成 exe 后，让生成的配置文件永久留在 exe 旁边） """
    if getattr(sys, 'frozen', False):
        # 如果是打包后的 exe，sys.executable 拿到的是生成的 main.exe 的绝对物理路径
        return os.path.dirname(os.path.abspath(sys.executable))
    else:
        # 如果是本地运行，拿到的是 main.py 源码的绝对路径
        return os.path.dirname(os.path.abspath(__file__))

# 规定： pet_settings.json 永远和你的运行软件紧紧呆在同一个物理文件夹下，拒绝被系统临时清理！
CONFIG_FILE = os.path.join(get_executable_dir(), "pet_settings.json")


# ==================== 异步大模型网络请求线程 ====================
class FetchReplyThread(QThread):
    """ 继承自 QThread 的独立线程流，专门用来处理连网大模型请求，防止主界面卡死崩溃 """
    finished_signal = pyqtSignal(str) # 定义信号：请求成功时，将回复文本传回主界面
    error_signal = pyqtSignal(str)    # 定义信号：请求失败时，将报错原因传回主界面

    def __init__(self, api_key, preset, prompt):
        super().__init__()
        self.api_key, self.preset, self.prompt = api_key, preset, prompt

    def run(self):
        """ 子线程的核心启动入口，在这里执行耗时的 HTTP 网络请求 """
        url = "https://api.deepseek.com/v1/chat/completions" # DeepSeek 标准 API 端点
        headers = {"Authorization": f"Bearer {self.api_key}", "Content-Type": "application/json"}
        data = {
            "model": "deepseek-chat", # 使用官方指定模型名
            "messages": [
                {"role": "system", "content": self.preset}, # 把你在后台设置的角色预设塞给系统
                {"role": "user", "content": self.prompt}     # 把你在快捷栏写的内容发给模型
            ]
        }
        try:
            # 发起 POST 联网请求，超时阈值设为 15 秒， verify=False 防止部分电脑证书报错
            response = requests.post(url, json=data, headers=headers, timeout=15, verify=False)
            if response.status_code == 200:
                # 状态码 200 说明请求大获成功，解析 JSON 文本拿到回复
                reply = response.json()['choices'][0]['message']['content']
                self.finished_signal.emit(reply) # 通过信号，把甜美的回复发回主界面
            else:
                self.error_signal.emit(f"API错误: {response.status_code}") # 触发错误信号
        except Exception as e:
            self.error_signal.emit(f"连接失败: {str(e)}") # 触发断网或连接失败信号


# ==================== 后台管理控制台面板 ====================
class MainDashboard(QWidget):
    """ 桌宠的后台综合管理控制中心 """
    def __init__(self, pet_instance):
        super().__init__()
        self.pet = pet_instance # 将桌宠本体的实例挂载到后台，方便两边互通数据
        self.setWindowTitle("桌宠管理后台")
        self.setWindowIcon(QIcon(get_resource_path("icon.ico"))) # 设置后台左上角图标
        self.resize(900, 600) # 后台默认宽 900 像素，高 600 像素
        self.init_ui()

    def init_ui(self):
        """ 初始化后台的整体排版和各个子功能页面 """
        main_layout = QHBoxLayout(self) # 采用水平大布局（左侧导航栏 + 右侧显示区）
        main_layout.setContentsMargins(0, 0, 0, 0) # 消除边缘白边
        main_layout.setSpacing(0)                  # 组件之间零缝隙

        # ---------------- 1. 左侧黑灰色导航栏 ----------------
        self.sidebar = QFrame()
        self.sidebar.setFixedWidth(200) # 固定导航栏宽度为 200 像素
        self.sidebar.setStyleSheet("background-color: #2e2e2e; color: white;")
        sidebar_layout = QVBoxLayout(self.sidebar) # 侧边栏内部使用垂直排队布局
        
        # 创建 6 大核心管理按钮
        self.btn_chat = QPushButton("💬 历史对话")
        self.btn_settings = QPushButton("⚙️ 系统设置")
        self.btn_help = QPushButton("❓ 功能说明")
        self.btn_reminders = QPushButton("⏰ 提醒事项")
        self.btn_chime = QPushButton("🔔 报时设置")
        self.btn_apps = QPushButton("🚀 快捷启动")
        
        # 批量美化按钮并推进侧邊栏
        for btn in [self.btn_chat, self.btn_settings, self.btn_help, self.btn_reminders, self.btn_chime, self.btn_apps]: 
            btn.setFixedHeight(50) # 按钮高度固定 50 像素
            btn.setStyleSheet("""
                QPushButton {
                    border: none; text-align: left; padding-left: 20px; font-size: 14px; color: white;
                } 
                QPushButton:hover { background-color: #404040; } /* 鼠标悬停时变浅灰色 */
            """)
            sidebar_layout.addWidget(btn)
        sidebar_layout.addStretch() # 在底部加一块弹簧，把所有按钮顶到最上面
        
        # ---------------- 2. 右侧多页面堆叠中心 ----------------
        self.stack = QStackedWidget()

        # --- 【页面 0】 历史对话历史管理 ---
        self.page_chat = QWidget()
        chat_layout = QHBoxLayout(self.page_chat)
        
        self.history_list = QListWidget() # 历史会话的左侧小列表
        self.history_list.setFixedWidth(200)
        self.history_list.setStyleSheet("background-color: #f5f5f5; border:none;")
        self.history_list.itemClicked.connect(self.load_history_detail) # 点击列表项载入对应对话
        self.history_list.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        self.history_list.customContextMenuRequested.connect(self.show_history_context_menu) # 开启右键菜单删除功能
        
        right_chat_vbox = QVBoxLayout() # 聊天展示区的垂直布局
        self.chat_display = QTextEdit() # 聊天大文本框
        self.chat_display.setReadOnly(True) # 只读，不可乱删
        self.chat_display.setStyleSheet("border:none; background: white; font-size:14px; padding:10px;")
        
        self.panel_input = QTextEdit() # 后台专门准备的测试对话框
        self.panel_input.setFixedHeight(100)
        self.panel_input.setPlaceholderText("在这里输入消息，桌宠也会同步回应...")
        
        btn_send = QPushButton("发送消息")
        btn_send.setFixedSize(100, 30)
        btn_send.clicked.connect(self.send_from_panel) # 后台发送联动
        
        right_chat_vbox.addWidget(self.chat_display)
        right_chat_vbox.addWidget(self.panel_input)
        right_chat_vbox.addWidget(btn_send, alignment=Qt.AlignmentFlag.AlignRight)
        
        chat_layout.addWidget(self.history_list)
        chat_layout.addLayout(right_chat_vbox)
        self.stack.addWidget(self.page_chat)

        # --- 【页面 1】 系统高级设置 ---
        self.page_settings = QWidget()
        settings_layout = QVBoxLayout(self.page_settings)
        settings_layout.setContentsMargins(40, 20, 40, 20)
        
        # 初始化读取各个设置组件的值
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
        
        # 窗口层级单选框设计（三选一）
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
        
        # 根据配置文件给对应的单选框打勾
        current_level = self.pet.config.get("window_level", "所有应用上方")
        if current_level == "所有应用上方": self.radio_top.setChecked(True)
        elif current_level == "非全屏应用上方": self.radio_normal.setChecked(True)
        elif current_level == "只显示在桌面": self.radio_bottom.setChecked(True)
        
        # 批量把传统文本输入项塞进页面排版
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
        
        # 托盘开关
        self.check_tray = QCheckBox("开启系统状态栏(托盘)图标")
        self.check_tray.setChecked(self.pet.config.get("tray_icon_enabled", True))
        settings_layout.addWidget(self.check_tray)
        
        # 底部保存总大按钮
        btn_save_all = QPushButton("保存所有设置")
        btn_save_all.setFixedHeight(40)
        btn_save_all.setStyleSheet("background-color: #007bff; color: white; font-weight: bold;")
        btn_save_all.clicked.connect(self.save_all_configs)
        settings_layout.addWidget(btn_save_all)
        settings_layout.addStretch()
        self.stack.addWidget(self.page_settings)

        # --- 【页面 2】 功能富文本说明页 ---
        self.page_help = QWidget()
        help_layout = QVBoxLayout(self.page_help)
        help_layout.setContentsMargins(40, 20, 40, 20)
        help_title = QLabel("🤖 桌宠使用与功能说明")
        help_title.setStyleSheet("font-size: 18px; font-weight: bold; color: #333;")
        help_content = QTextEdit()
        help_content.setReadOnly(True)
        help_content.setStyleSheet("border: 1px solid #ccc; background: #fdfdfd; font-size: 14px; padding: 15px; line-height: 1.6;")
        # 完美植入由你指定的爱莉希雅风格精美功能富文本
        help_content.setHtml("""
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
        help_layout.addWidget(help_content)
        self.stack.addWidget(self.page_help)

        # --- 【页面 3】 提醒事项(日程管理) ---
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

        # --- 【页面 4】 爱莉风格整点报时语录文本管理 ---
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
        
        # --- 【页面 5】 右键应用快捷启动管理页面 ---
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
        
        # 将左侧侧边栏、右侧核心容器整体推入大屏幕
        main_layout.addWidget(self.sidebar)
        main_layout.addWidget(self.stack)

        # === 核心闭环：完美对齐 6 个按钮和右侧单页容器的映射，永不发生界面错位 ===
        self.btn_chat.clicked.connect(lambda: self.stack.setCurrentIndex(0))
        self.btn_settings.clicked.connect(lambda: self.stack.setCurrentIndex(1))
        self.btn_help.clicked.connect(lambda: self.stack.setCurrentIndex(2))
        self.btn_reminders.clicked.connect(lambda: [self.stack.setCurrentIndex(3), self.refresh_reminders()])
        self.btn_chime.clicked.connect(lambda: [self.stack.setCurrentIndex(4), self.load_chime_to_ui()])
        self.btn_apps.clicked.connect(lambda: [self.stack.setCurrentIndex(5), self.refresh_apps()])

    def closeEvent(self, event):
        """ 拦截后台的右上角关闭按钮事件：改为仅仅假隐藏，保护后台驻留内存 """
        event.ignore()  
        self.hide()     

    def refresh_history(self):
        """ 刷新历史对话卡片，采用最新对谈排在最上面的逻辑 """
        self.history_list.clear()
        for i, item in enumerate(reversed(self.pet.config.get("history", []))):
            title = item["user"][:12] + ("..." if len(item["user"]) > 12 else "")
            actual_index = len(self.pet.config['history']) - 1 - i
            list_item = QListWidgetItem(f"对谈 {actual_index + 1}\n{title}")
            list_item.setData(Qt.ItemDataRole.UserRole, actual_index)
            self.history_list.addItem(list_item)

    def show_history_context_menu(self, pos):
        """ 建立列表右键小菜单：实现点击右键可以剔除对话 """
        item = self.history_list.itemAt(pos)
        if item is None: return
        menu = QMenu(self)
        delete_action = QAction("❌ 删除此条对话记录", self)
        actual_index = item.data(Qt.ItemDataRole.UserRole)
        delete_action.triggered.connect(lambda: self.delete_history_item(actual_index))
        menu.addAction(delete_action)
        menu.exec(QCursor.pos())

    def delete_history_item(self, index):
        """ 执行后台数据删除：同步擦除内存和 json 本地文件 """
        if 0 <= index < len(self.pet.config.get("history", [])):
            self.pet.config["history"].pop(index)
            self.pet.save_config()
            self.chat_display.clear() 
            self.refresh_history()    

    def load_history_detail(self, item):
        """ 在大文本域里完整复现你点击的某条历史对话细节 """
        actual_index = item.data(Qt.ItemDataRole.UserRole)
        data = self.pet.config["history"][actual_index]
        self.chat_display.setPlainText(f"主人: {data['user']}\n\n桌宠: {data['pet']}")

    def send_from_panel(self):
        """ 允许指挥官直接在控制台和桌宠对话发送 """
        text = self.panel_input.toPlainText().strip()
        if text:
            self.pet.call_api(text)
            self.panel_input.clear()
            self.chat_display.append(f"\n主人: {text}\n(正在等待桌宠回应...)")

    def refresh_reminders(self):
        """ 刷新加载日程列表 """
        self.reminder_list.clear()
        for i, rem in enumerate(self.pet.config.get("reminders", [])):
            self.reminder_list.addItem(f"⏰ [{rem['time']}] {rem['content']}")

    def add_reminder_dialog(self):
        """ 后台手动弹窗录入新日程 """
        time_str, ok1 = QInputDialog.getText(self, "添加日程", "请输入时间 (格式 如 14:30):")
        if not ok1 or not time_str: return
        content, ok2 = QInputDialog.getText(self, "添加日程", "请输入提醒内容:")
        if not ok2 or not content: return
        
        self.pet.config["reminders"].append({"time": time_str.strip(), "content": content.strip()})
        self.pet.save_config()
        self.refresh_reminders()

    def delete_reminder(self):
        """ 从后台清除选中的未触发日程 """
        current_row = self.reminder_list.currentRow()
        if current_row >= 0:
            self.pet.config["reminders"].pop(current_row)
            self.pet.save_config()
            self.refresh_reminders()

    def save_chime_state(self, state):
        """ 保存整点报时开启/关闭的勾选状态 """
        self.pet.config["hourly_chime"] = self.check_chime.isChecked()
        self.pet.save_config()

    def load_chime_to_ui(self):
        """ 把爱莉希雅繁琐的 24 小时报时字典重新解压为可编辑的可视化每行文本 """
        text_lines = []
        messages = self.pet.config.get("hourly_messages", {})
        for hour, msgs in messages.items():
            for msg in msgs:
                text_lines.append(f"{hour}|{msg}")
        self.chime_edit.setPlainText("\n".join(text_lines))

    def save_chime_messages(self):
        """ 将大输入域里你修改过的报时文本逆向封装回字典中储存 """
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
        """ 【重写保存核心】：读取后台所有的单选按钮、文本和参数并存入 json 并令桌宠即时生效 """
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
        
        # 核心：通过判断哪个单选框亮着来重塑置顶级别属性
        if self.radio_top.isChecked():
            self.pet.config["window_level"] = "所有应用上方"
        elif self.radio_normal.isChecked():
            self.pet.config["window_level"] = "非全屏应用上方"
        elif self.radio_bottom.isChecked():
            self.pet.config["window_level"] = "只显示在桌面"
            
        self.pet.save_config()
        
        # 命令桌宠窗口外壳瞬间换血：变更透明度、修改置顶级别、调整托盘状态
        self.pet.apply_window_level()
        self.pet.reset_opacity()
        self.pet.update_tray_icon_status()
        
        self.pet.show_message("✨ 设置已同步！")

    def refresh_apps(self):
        """ 后台刷新软件启动列表 """
        self.apps_list.clear()
        for app in self.pet.config.get("quick_apps", []):
            self.apps_list.addItem(f"📂 {app['name']} ➔ {app['path']}")

    def add_app_dialog(self):
        """ 弹出标准的文件搜索窗，供用户添加外部软件绑定快捷打开 """
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
        """ 清除不想在桌宠右键菜单看到的外部程序 """
        current_row = self.apps_list.currentRow()
        if current_row >= 0:
            self.pet.config["quick_apps"].pop(current_row)
            self.pet.save_config()
            self.refresh_apps()


# ==================== 桌宠核心物理窗口 ====================
class DesktopPet(QWidget):
    """ 桌宠的主演变实体：管理拖动、尺寸调整、时钟监听、系统指令和模型回复 """
    def __init__(self):
        super().__init__()
        self.m_drag = False       # 标记：是否正在进行鼠标左键拖运位移
        self.m_resize = False     # 标记：是否正在右键拉扯边缘调整大小
        self.is_typing = False    # 标记：气泡打字机特效是否正在运作
        self.last_click_time = 0  # 记录上一次左键敲击的时间戳
        self.orig_width, self.orig_height = 220, 220 # 牢固锁定爱莉希雅基础的黄金物理宽高比例
        self.last_hour = -1       # 拦截器：上一次播报整点的小时，防止在一分钟内疯狂报时
        
        self.load_config()                 # 1. 优先载入安全无重置配置
        self.dashboard = MainDashboard(self) # 2. 将后台实例注入桌宠肚子里
        self.init_ui()                     # 3. 初始化无边框、物理坐标组件
        self.init_timers()                 # 4. 开启四大常驻轮询后台时钟
        self.tray_icon = None              # 初始化托盘对象为空
        self.update_tray_icon_status()     # 5. 根据勾选状态动态唤醒状态栏图标
        self.setWindowIcon(QIcon(get_resource_path("icon.ico")))

    def update_tray_icon_status(self):
        """ 建立状态栏托盘：实现在任务栏角落默默驻扎 """
        enabled = self.config.get("tray_icon_enabled", True)
        
        if enabled:
            if not self.tray_icon:
                self.tray_icon = QSystemTrayIcon(self)
                self.tray_icon.setIcon(QIcon(get_resource_path("icon.ico")))
                self.tray_icon.setToolTip("爱莉希雅桌宠 ♪")
                
                tray_menu = QMenu()
                tray_menu.setStyleSheet("QMenu { background: white; border: 1px solid #ccc; }")
                
                # 配置状态栏托盘上的右键快捷功能跳转
                a0 = QAction("💬 历史对话面板", self)
                a0.triggered.connect(lambda: self.open_dashboard(0))
                a1 = QAction("⚙️ 设定修改", self)
                a1.triggered.connect(lambda: self.open_dashboard(1))
                a3 = QAction("⏰ 提醒事项", self)
                a3.triggered.connect(lambda: self.open_dashboard(3))
                a4 = QAction("🔔 报时设置", self)
                a4.triggered.connect(lambda: self.open_dashboard(4))
                
                # 防窗口走丢特效药按钮
                a_reset = QAction("🔄 重置位置与强制置顶", self)
                a_reset.triggered.connect(self.reset_pet_position)
                
                a_exit = QAction("❌ 退出应用", self)
                a_exit.triggered.connect(lambda: sys.exit(0)) # 暴力绝杀所有后台线程，完全退出
                
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
        """ 救急引擎：防 Win+D 或全屏应用切回导致窗体被底层系统强制神隐 """
        self.move(1400, 400) # 将桌宠强行从虚无深渊中拉回屏幕（1400, 400）右下角坐标
        self.apply_window_level() # 强行赋予置顶光环
        self.reset_opacity()      # 强行拉回 100% 显形状态
        self.show()
        self.raise_()
        self.activateWindow()     # 获取操作系统的焦点
        self.show_message("妖精小姐闪现回来啦！没有让你久等吧~ ♪")
        self.change_state("wink")

    def load_config(self):
        """ 防御式数据初始化引擎：增量补全，绝不破坏覆盖用户的已有设置 """
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
                "06": ["清晨六点整！呼哈~ 太阳升起啦，新的新一天，也要充满对爱莉希雅的期待哦~"],
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

                # 如果读取的是以前的老版本 json，安全注入缺失的快捷启动数组，防止底层闪退
                if "quick_apps" not in self.config:
                    self.config["quick_apps"] = []
                
                # 增量安全检查：只补全没有的键，坚决保护已有 API_KEY 等核心参数
                for key, value in default_config.items():
                    if key not in self.config:
                        self.config[key] = value
                return # 成功归来，断开下面重写空文件的逻辑
                
            except Exception as e:
                print(f"读取配置失败，将重建配置。原因: {e}")
                self.config = default_config
        else:
            self.config = default_config
            
        self.save_config()

    def check_time_events(self):
        """ 高频时间事务管家：每秒准时检查是否该报时或拉起日程提醒 """
        now = datetime.now()
        current_time = now.strftime("%H:%M")
        
        # 1. 检查整点报时：只有在第 0 分 0 秒时才会准许通过
        if self.config.get("hourly_chime", True) and now.minute == 0 and now.second == 0:
            if now.hour != self.last_hour:
                self.last_hour = now.hour
                hour_str = str(now.hour).zfill(2)
                # 随机挑选当前小时你预设的爱莉语录中的一句冒泡展示
                msgs = self.config.get("hourly_messages", {}).get(hour_str, [f"铛铛！已经{now.hour}点整了哦~"])
                msg = random.choice(msgs)
                self.show_message(msg)
                self.change_state("wink")

        # 2. 检查日程事项列表
        for rem in self.config.get("reminders", []):
            if rem["time"] == current_time and now.second == 0:
                self.show_message(f"⏰ 亲爱的！妖精小姐来提醒你：\n『 {rem['content']} 』")
                self.change_state("hurry")
                # 触发无视一切的系统最高优先级强制同步置顶弹框
                QTimer.singleShot(100, lambda r=rem: QMessageBox.information(None, "⏰ 日程提醒", f"爱莉希雅提醒你：\n\n{r['content']}"))

    def init_ui(self):
        """ 桌宠物理多组件渲染中心 """
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground) # 开启窗口全透明背景支持
        
        # 渲染 1号挂载件：对话冒泡框
        self.bubble = QLabel(self)
        self.bubble.setWordWrap(True)  # 支持文本内部自动根据边界进行折行
        self.bubble.setStyleSheet("""
            background-color: rgba(255, 255, 255, 0.95); border: 3px solid #FFB6C1; 
            border-radius: 16px; padding: 16px; font-family: 'Microsoft YaHei'; font-size: 16px; color: #333333;
        """)
        self.bubble.hide()

        # 渲染 2号挂载件：桌宠身体大容器
        self.pet_label = QLabel(self)
        self.pet_label.setScaledContents(True) # 允许动图随着外部容器拉宽而等比例拉大
        init_path = get_resource_path("assets/waiting.gif")
        if os.path.exists(init_path):
            self.movie = QMovie(init_path)
            self.pet_label.setMovie(self.movie)
            self.movie.start()
        else:
            self.pet_label.setText("【素材丢失】")

        # 渲染 3号挂载件：聊天输入快捷栏
        self.input_box = QLineEdit(self)
        self.input_box.setPlaceholderText("聊点什么？或输入'12:00提醒我吃饭'")
        # 注意：这里不再写死高度和字体，交给物理引擎 update_window_total_size 统一支配
        self.input_box.hide()

        self.resize(self.orig_width, self.orig_height) # 初始外壳设为 220x220
        self.update_window_total_size()                # 立刻驱动一次物理排版公式
        self.move(1400, 400)                           # 降落在屏幕右下方
        self.apply_window_level()                      # 应用后台存储的层级样式

    def init_timers(self):
        """ 建立四大永恒后台守护时钟线 """
        # 时钟A：每隔一分钟监测是否入夜，入夜自动切换为 sleep 睡眠动态
        self.state_timer = QTimer(self)
        self.state_timer.timeout.connect(self.check_time_state)
        self.state_timer.start(60000)

        # 时钟B：每隔 1秒执行一次时间事务总线（报时+日程弹框）
        self.time_check_timer = QTimer(self)
        self.time_check_timer.timeout.connect(self.check_time_events)
        self.time_check_timer.start(1000) 

        # 时钟C：无操作挂机闲置检测时钟，触发唠叨语
        self.idle_count = 0
        self.idle_timer = QTimer(self)
        self.idle_timer.timeout.connect(self.check_idle)
        self.idle_timer.start(1000)

        # 时钟D：冒泡气泡打字完毕后的 3秒展示淡出单次时钟
        self.fade_timer = QTimer(self)
        self.fade_timer.setSingleShot(True)
        self.fade_timer.timeout.connect(self.fade_out_bubble)

        # 时钟E：快捷输入框唤醒后 10秒无操作隐形时钟
        self.input_hide_timer = QTimer(self)
        self.input_hide_timer.setSingleShot(True)
        self.input_hide_timer.timeout.connect(self.auto_hide_input_box)

        # 时钟F：专门用来计算鼠标离开后，桌宠何时执行渐变半透明影分身的时钟
        self.opacity_timer = QTimer(self)
        self.opacity_timer.timeout.connect(self.check_opacity_timeout)
        self.opacity_timer.start(1000) 
        self.user_active_count = 0     

    def apply_window_level(self):
        """ 【物理层级管控中心】：通过无边框与底层或顶层标记配合，斩断操作系统的干扰 """
        level = self.config.get("window_level", "所有应用上方")
        base_flags = Qt.WindowType.FramelessWindowHint | Qt.WindowType.Tool
        
        if level == "所有应用上方":
            # 赐予强置顶标签：无视一切打开的浏览器、游戏，强制在视线最前端
            self.setWindowFlags(base_flags | Qt.WindowType.WindowStaysOnTopHint)
        elif level == "非全屏应用上方":
            # 赐予普通置顶：在遇到全屏看电影或玩游戏时，会自动体贴地没入其下方，不阻挡视线
            self.setWindowFlags(base_flags | Qt.WindowType.WindowStaysOnTopHint)
        elif level == "只显示在桌面":
            # 剥夺所有置顶标签，将其降级为地底层窗口，会被任何普通软件无情遮盖
            self.setWindowFlags(base_flags | Qt.WindowType.WindowStaysOnBottomHint)
            
        self.show() # 重构核心窗口标记后，必须强制唤醒一次显形，否则窗口会被系统直接抹去

    def check_opacity_timeout(self):
        """ 监测无操作状态：让爱莉希雅像妖精魔法一样虚化成影子 """
        if not self.config.get("opacity_enabled", True): return
            
        # 核心拦截：如果鼠标正好搭在桌宠身上，或者输入框内正在疯狂打字，强行保鲜，拒绝变透明
        if self.underMouse() or (self.input_box.isVisible() and self.input_box.hasFocus()):
            self.reset_opacity()
            return
            
        self.user_active_count += 1
        
        # 挂机倒计时走完，依法激活不透明度削减
        if self.user_active_count >= self.config.get("opacity_timeout", 5):
            target_opacity = self.config.get("opacity_value", 0.3)
            target_opacity = max(0.1, min(1.0, target_opacity)) # 阀值保护：最极限也留 10% 轮廓，防止彻底消失找不回来
            self.setWindowOpacity(target_opacity)

    def reset_opacity(self):
        """ 瞬间打破透明影分身：满血拉回 100% 显性状态并重设挂机倒数 """
        self.user_active_count = 0
        self.setWindowOpacity(1.0)

    def enterEvent(self, event):
        """ 重写系统鼠标进入区域函数：鼠标只要稍微一触碰到桌宠，身体立刻瞬间亮起，恢复精神 """
        self.reset_opacity()
        super().enterEvent(event)

    def leaveEvent(self, event):
        """ 重写系统鼠标离开区域函数：立刻重新规划新一轮的自动虚化倒数 """
        self.user_active_count = 0
        super().leaveEvent(event)

    def check_time_state(self):
        """ 夜晚环境判定：深夜十点到清晨八点，爱莉会自动趴着睡大觉 """
        hour = datetime.now().hour
        if hour >= 22 or hour < 8: self.change_state("sleep")

    def check_idle(self):
        """ 挂机唠叨检查 """
        self.idle_count += 1
        if self.idle_count >= self.config.get("idle_timeout", 1200):
            self.show_message("主人，你还在忙吗？默默陪着你...")
            self.idle_count = 0
            self.change_state("hurry")

    def auto_hide_input_box(self):
        """ 10秒不理会输入框时，清空内容并缩回衣袖隐藏 """
        if self.input_box.isVisible():
            self.input_box.clear()
            self.input_box.hide()
            self.update_window_total_size()

    def update_window_total_size(self):
        """ 【🌟 核心黄金物理布局排版引擎】：完美解决启动与缩放后输入框不一致的终极特效药 """
        current_width = self.width() # 读取现在外壳真实的物理横向跨度
        scale_ratio = self.orig_height / self.orig_width # 计算黄金高宽比（1.0）
        pet_height = int(current_width * scale_ratio)     # 算出当前宠物应变的高度
        scale_factor = current_width / self.orig_width    # 动态抓取缩放倍率系数

        # ----------------- 1. 物理测绘一楼：对话冒泡框 -----------------
        bubble_height = 0
        if self.bubble.isVisible():
            self.bubble.setMaximumWidth(int(current_width * 2)) 
            bubble_height = self.bubble.sizeHint().height()
            self.bubble.setGeometry(0, 0, current_width, bubble_height)
        
        # ----------------- 2. 物理测绘二楼：桌宠动图标签 -----------------
        # 它的 Y轴（纵向高度）死死钉在气泡的脚底下（bubble_height），两层界限分明，绝不发生侵占
        self.pet_label.setGeometry(0, bubble_height, current_width, pet_height)
        
        # ----------------- 3. 物理测绘三楼：核心输入快捷栏 -----------------
        input_height = 0
        if self.input_box.isVisible():
            # === 核心修改：让启动状态与缩放状态在这里使用同一个数学公式集中管控 ===
            new_input_width = int(200 * scale_factor)                        # 220 宽时对应 200px 宽
            new_input_height = max(20, int(30 * scale_factor))               # 220 宽时对应 30px 高
            new_input_font = max(10, int(13 * scale_factor))                # 220 宽时对应 13px 字体
            
            # 实施集中制裁美化：一处修改，两端永远对齐
            self.input_box.setFixedSize(new_input_width, new_input_height)
            self.input_box.setStyleSheet(f"""
                font-size: {new_input_font}px; height: {new_input_height}px; padding: 4px; 
                border: 2px solid #FFB6C1; border-radius: {max(4, int(6*scale_factor))}px; background: white;
            """)
            
            input_height = self.input_box.height()
            input_width = self.input_box.width()
            # 数学公式：外壳宽减去自己宽除以2，算出完美居中的水平 X轴坐标
            input_x = (current_width - input_width) // 2
            # 它的 Y轴钉在：气泡高度 + 宠物高度 + 10像素的安全空气隔离带
            self.input_box.setGeometry(input_x, bubble_height + pet_height + 10, input_width, input_height)
            
        # 叠罗汉总长 = 气泡总高 + 身体高 + （如果有输入框的话？输入框高+20底边留空 ： 没有的话直接0）
        total_height = bubble_height + pet_height + (input_height + 20 if input_height > 0 else 0)
        # 用强固定尺寸 setFixedSize 锁死系统外框。由于没有了布局掐架，缩放与启动的比例会惊人地完美一致！
        self.setFixedSize(current_width, total_height)

    def mousePressEvent(self, event):
        """ 重写鼠标按下事件：掌管左键唤醒输入框、左键准备拖运、右键准备等比缩放 """
        self.reset_opacity() # 打破虚化
        current_time = datetime.now().timestamp()
        
        # 保险拦截：如果鼠标正好在点输入框内部，维持十秒倒计时的清醒，不要隐形
        if self.input_box.geometry().contains(event.pos()) and not self.input_box.isHidden():
            self.input_hide_timer.start(10000)
            event.accept()
            return

        # 打字机特效点击拦截加速：如果字还没吐完点击，瞬间亮出所有满文
        if self.bubble.isVisible():
            if self.is_typing:
                self.typewriter_timer.stop()
                self.bubble.setText(self.full_text)
                self.is_typing = False
                self.fade_timer.start(3000)
                event.accept()
                return
            elif current_time - self.last_click_time > 0.5:
                # 再次快速点击，冒泡主动消失
                self.fade_timer.stop()
                self.fade_out_bubble()
                event.accept()
                return

        if event.button() == Qt.MouseButton.LeftButton:
            self.m_drag = True
            # 精密锚定算法：鼠标点下的全局高精物理坐标，减去当前窗口左上角物理坐标，算出永不偏移的矢量摇杆
            self.m_DragPosition = event.globalPosition().toPoint() - self.pos()
            
            # 单击判定：弹出或隐藏输入快捷栏
            if self.input_box.isHidden():
                self.input_box.show()
                self.input_box.setFocus()
                self.input_hide_timer.start(10000)
            else:
                if not self.input_box.text().strip():
                    self.input_box.hide()
                    self.input_hide_timer.stop()
            
            self.update_window_total_size() # 排版引擎冲锋刷新
            event.accept()

        elif event.button() == Qt.MouseButton.RightButton:
            # 右键准备等比拉伸拉大
            self.m_resize = True
            self.start_pos = event.globalPosition().toPoint() 
            self.start_width, self.start_height = self.width(), self.height()                 
            event.accept()

    def mouseMoveEvent(self, event):
        """ 重写鼠标移动事件：这里执行真正的移位和强力等比无缝缩放 """
        if Qt.MouseButton.LeftButton and self.m_drag:
            # 搬运位移：跟随鼠标当前全局最新坐标，减去刚才记录的固定摇杆点，顺滑滑行
            self.move(event.globalPosition().toPoint() - self.m_DragPosition)
            event.accept()
            
        elif Qt.MouseButton.RightButton and self.m_resize:
            current_pos = event.globalPosition().toPoint()
            # 算盘：算鼠标横向平移了多少物理像素点
            delta_x = current_pos.x() - self.start_pos.x() 
            
            new_width = self.start_width + delta_x
            if new_width < 120: new_width = 120 # 节制下限：不要缩得比小布丁还小
            if new_width > 800: new_width = 800 # 节制上限：不要把整个屏幕塞满
            
            # 注意：此处只负责改变外层主壳的真实横向宽度，样式更新早已全部平移托管到下方的 update_window_total_size 中
            self.setFixedSize(new_width, self.height())
            self.update_window_total_size() # 完美的集中物理公式在此自动渲染出完美等比例
            event.accept()
    
    def mouseReleaseEvent(self, event):
        """ 鼠标松开释放事件 """
        self.m_drag = False
        if event.button() == Qt.MouseButton.RightButton and self.m_resize:
            self.m_resize = False
            # 如果右键按住只是轻微晃动或没动（距离小于5像素），判定为单纯的右键单击，呼出菜单
            distance = (event.globalPosition().toPoint() - self.start_pos).manhattanLength()
            if distance < 5: self.popup_menu(event)
            event.accept()

    def keyPressEvent(self, event):
        """ 键盘事件拦截：如果用户敲击的是大回车键，且快捷聊天框正亮着，执行消息发射 """
        if event.key() in (Qt.Key.Key_Return, Qt.Key.Key_Enter):
            if self.input_box.isVisible() and self.input_box.hasFocus():
                self.input_box.hide()
                self.input_hide_timer.stop() 
                self.handle_input()
                event.accept()
                return
        super().keyPressEvent(event)

    def popup_menu(self, event):
        """ 桌宠公仔身体右键弹出的大菜单 """
        menu = QMenu(self)
        menu.setStyleSheet("QMenu { background: white; border: 1px solid #ccc; padding: 5px; }")
        
        # 1. 遍历你存在 json 里的快捷应用，实时且动态地注入菜单的最顶层！
        quick_apps = self.config.get("quick_apps", [])
        if quick_apps:
            for app in quick_apps:
                app_action = QAction(f"🚀 {app['name']}", self)
                # 高级闭包技术：利用 p=app['path'] 将路径锁进 lambda 作用域，防止点击时全部变成最后一个路径
                app_action.triggered.connect(lambda chk, p=app['path']: self.launch_exe(p))
                menu.addAction(app_action)
            menu.addSeparator() 

        # 2. 原本的系统综合选项
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
        """ 快捷软件核心唤醒代码 """
        if os.path.exists(exe_path):
            try:
                # 使用 subprocess.Popen 独立沙箱级别进程唤醒外部 exe，两边互不干扰，即便游戏闪退也绝不波及桌宠
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
        """ 优雅地引导并展示对应的后台综合子管理页面 """
        self.dashboard.stack.setCurrentIndex(tab_index)
        if tab_index == 0: self.dashboard.refresh_history()
        elif tab_index == 3: self.dashboard.refresh_reminders()
        elif tab_index == 4: self.dashboard.load_chime_to_ui()
        elif tab_index == 5: self.dashboard.refresh_apps()
        self.dashboard.show()
        self.dashboard.raise_()

    def change_state(self, state):
        """ 精密状态转换机：负责卸载老动画，安全平移加载对应情感的新动作 gif """
        paths = {"sleep": "assets/sleep.gif", "waiting": "assets/waiting.gif", "cry": "assets/cry.gif", "question": "assets/question.gif", "wink": "assets/wink.gif", "like": "assets/like.gif", "speechless": "assets/speechless.gif", "hurry": "assets/hurry.gif"}
        target_path = get_resource_path(paths.get(state, "assets/waiting.gif"))
        if os.path.exists(target_path):
            self.movie.stop()
            self.movie.setFileName(target_path)
            self.movie.start()

    def handle_input(self):
        """ 指令拦截与信息发射决策器 """
        self.reset_opacity()
        text = self.input_box.text().strip()
        if not text: 
            self.update_window_total_size()
            return
        self.input_box.clear()

        # 核心拦截分支 A：高级高精正则表达式拦截“XX:XX提醒我XXXX”声控日程设置
        import re
        time_match = re.search(r'(\d{1,2}[:：]\d{2})', text)
        if time_match and "提醒" in text:
            rem_time = time_match.group(1).replace("：", ":") # 自动兼容中英文输入法冒号
            content = text.split("提醒我")[-1].strip()
            self.config["reminders"].append({"time": rem_time, "content": content})
            self.save_config()
            self.show_message(f"已经记在心里啦，{rem_time} 准时叫你哦~ ♪")
            self.change_state("wink")
            return
        
        # 核心拦截分支 B：斜杠“/”声控开启常用快捷系统管理面板
        if text.startswith("/"):
            tools = {
                "cmd": "cmd", "命令行": "cmd", "终端": "cmd",
                "计算器": "calc", "记事本": "notepad", "画图": "mspaint",
                "控制面板": "control", "设备管理器": "devmgmt.msc", "服务": "services.msc",
                "任务管理器": "taskmgr", "注册表": "regedit", "磁盘清理": "cleanmgr",
                "计算机管理": "compmgmt.msc", "事件": "eventvwr.msc", "配置诊断": "dxdiag",
                "网络": "ncpa.cpl", "网卡设置": "ncpa.cpl", "防火墙": "control firewall.cpl"
            }
            cmd_key = text[1:].strip().lower() # 去掉斜杠，化为小写
            
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
            
        # 核心分支 C：没有触发任何系统拦截，正式移交子线程发起连网 AI 大模型交互
        else:
            self.call_api(text)

    def call_api(self, prompt):
        """ 连网大模型准备就绪阶段 """
        api_key = self.config.get("api_key", "")
        if not api_key:
            self.show_message("请先绑定你的 API Key。")
            return
        self.show_message("思考中...")
        self.change_state("question") # 歪头陷入苦思冥想动态
        
        # 启动异步独立子线程流
        self.api_thread = FetchReplyThread(api_key, self.config["role_preset"], prompt)
        self.api_thread.finished_signal.connect(lambda reply: self.on_api_success(prompt, reply))
        self.api_thread.error_signal.connect(lambda err: self.show_message(err))
        self.api_thread.start()

    def on_api_success(self, prompt, reply):
        """ 异步网络请求凯旋而归：分离大模型回复里的心情标签，执行动作同步映射 """
        state = "wink"
        import re
        # 精准匹配文末类似 [cry] 或 [like] 的心情彩蛋小尾巴
        match = re.search(r'\[(waiting|cry|question|wink|like|speechless|hurry)\]', reply)
        if match:
            state = match.group(1) # 获取提取出的感情动作关键词
            # 用正则表达式把丑陋的方括号心情标签从纯净的对话文本中干净滤除
            reply = re.sub(r'\[(waiting|cry|question|wink|like|speechless|hurry)\]', '', reply).strip()
        
        self.change_state(state) # 动态无缝切入大模型为你分配的情绪动作中！
        self.show_message(reply) # 打字机开始吐字
        
        # 实时同步追加录入到控制台的历史页面中并存盘
        self.dashboard.chat_display.append(f"\n主人: {prompt}\n桌宠: {reply}\n" + "-"*30)
        self.config["history"].append({"user": prompt, "pet": reply})
        self.save_config()
        self.dashboard.refresh_history()

    def show_message(self, text):
        """ 打字机特效初始化配置 """
        self.fade_timer.stop()
        self.bubble.show()
        self.full_text = text
        self.current_idx = 0
        self.is_typing = True
        
        if hasattr(self, 'typewriter_timer') and self.typewriter_timer.isActive():
            self.typewriter_timer.stop()
        self.typewriter_timer = QTimer(self)
        self.typewriter_timer.timeout.connect(self.update_typewriter)
        self.typewriter_timer.start(40) # 每 40 毫秒吐出一个汉字，节奏轻快美妙

    def update_typewriter(self):
        """ 定时产生逐字吐露的打字机视感特效 """
        if self.current_idx <= len(self.full_text):
            self.bubble.setText(self.full_text[:self.current_idx])
            self.current_idx += 1
            self.update_window_total_size() # 吐字过程中，物理排版引擎高频跟随字数动态调整高矮，极其丝滑！
        else:
            self.typewriter_timer.stop()
            self.is_typing = False
            self.last_click_time = datetime.now().timestamp()
            self.fade_timer.start(3000) # 吐完字后，在屏幕大方留存 3秒供舰长阅读，然后自动淡化

    def fade_out_bubble(self):
        """ 冒泡气泡功成身退假隐藏，重调环境情感 """
        self.bubble.hide()
        self.update_window_total_size() # 隐藏气泡后，窗口高矮立刻缩回基础矮个子状态，让出屏幕资源
        hour = datetime.now().hour
        if hour >= 22 or hour < 8: self.change_state("sleep")
        else: self.change_state("waiting") # 重新转入老实的静默等待待机动图中


if __name__ == '__main__':
    # 操作系统底层的 PyQt 进程总线启动入口
    app = QApplication(sys.argv)
    pet = DesktopPet()
    pet.show() # 令可爱的爱莉希雅在你的屏幕闪亮登场！
    sys.exit(app.exec())