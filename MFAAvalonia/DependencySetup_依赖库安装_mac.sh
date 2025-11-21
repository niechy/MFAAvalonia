#!/bin/bash

# 启用颜色输出
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
WHITE='\033[1;37m'
BOLD='\033[1m'
NC='\033[0m' # 重置颜色

# 初始化错误标志和架构变量
error_occurred=0
arch="x64"
dotnet_install_dir="/usr/share/dotnet"  # 系统级安装目录（全局可用）
dotnet_script_path="/tmp/dotnet-install.sh"  # 临时脚本路径

# 检测系统架构（x64/arm64）
detect_arch() {
    local uname_arch=$(uname -m)
    case $uname_arch in
		x64|x86_64|amd64) arch="x64" ;;  # 兼容 amd64（部分系统输出）
        aarch64|arm64) arch="arm64" ;;  # 同时匹配 aarch64 和 arm64
        *) 
            echo -e "${RED}不支持的架构: $uname_arch${NC}"
            exit 1 
            ;;
    esac
    echo -e "${BOLD}${BLUE}检测到系统架构: $arch${NC}"
}

# 检查并获取管理员权限（系统级安装必需）
check_admin() {
    if [ "$EUID" -ne 0 ]; then
        echo -e "${YELLOW}需要管理员权限（系统级安装），即将请求 sudo 密码...${NC}"
        sudo "$0" "$@"
        exit $?
    fi
}

# 基于官方 dotnet-install.sh 脚本安装 .NET Runtime 10
install_dotnet() {
    echo -e "\n${BLUE}===================================================================================================="
    echo -e "${BOLD}${CYAN}正在通过官方脚本安装 .NET Runtime 10 ($arch)${NC}"
    echo -e "${BOLD}${CYAN}Installing .NET Runtime 10 ($arch) via official script${NC}"
    echo -e "${BLUE}===================================================================================================="${NC}

    # 步骤1：安装依赖工具 wget（如果未安装）
    echo -e "${YELLOW}1/5 检查并安装依赖工具 wget...${NC}"
    if ! command -v wget &> /dev/null; then
        echo -e "${YELLOW}wget 未安装，正在通过 apt 安装...${NC}"
        apt-get update > /dev/null 2>&1  # 更新包列表
        apt-get install -y wget > /dev/null 2>&1
        if [ $? -ne 0 ]; then
            error_occurred=1
            echo -e "${RED}❌ 安装 wget 失败，请检查网络连接或包管理器配置${NC}"
            return
        fi
        echo -e "${GREEN}✅ wget 安装成功${NC}"
    else
        echo -e "${GREEN}✅ wget 已安装${NC}"
    fi

    # 步骤2：下载官方 dotnet-install.sh 脚本
    echo -e "${YELLOW}2/5 下载官方 dotnet-install.sh 脚本...${NC}"
    wget -q -O "$dotnet_script_path" https://dot.net/v1/dotnet-install.sh  # -q 静默下载
    if [ $? -ne 0 ] || [ ! -f "$dotnet_script_path" ]; then
        error_occurred=1
        echo -e "${RED}❌ 下载官方脚本失败，请检查网络连接（推荐科学上网）${NC}"
        return
    fi
    echo -e "${GREEN}✅ 官方脚本下载成功${NC}"

    # 步骤3：授予脚本执行权限
    echo -e "${YELLOW}3/5 授予脚本执行权限...${NC}"
    chmod +x "$dotnet_script_path"
    if [ $? -ne 0 ]; then
        error_occurred=1
        echo -e "${RED}❌ 授予脚本执行权限失败${NC}"
        return
    fi
    echo -e "${GREEN}✅ 权限授予成功${NC}"

    # 步骤4：运行官方脚本安装 .NET Runtime 10
    echo -e "${YELLOW}4/5 安装 .NET Runtime 10（可能需要几分钟，取决于网络速度）...${NC}"
    "$dotnet_script_path" \
        --channel 10.0 \          # 指定安装 10.x 版本通道
        --runtime dotnet \        # 仅安装运行时（如需 SDK 可改为 --sdk）
        --install-dir "$dotnet_install_dir" \  # 系统级安装目录（全局可用）
        --architecture "$arch" \  # 指定架构（与检测结果一致）
        --quiet                   # 静默安装（减少输出）
    if [ $? -ne 0 ]; then
        error_occurred=1
        echo -e "${RED}❌ .NET Runtime 10 安装失败${NC}"
        return
    fi
    echo -e "${GREEN}✅ .NET Runtime 10 安装完成${NC}"

    # 步骤5：配置全局环境变量（所有用户可用）
    echo -e "${YELLOW}5/5 配置全局环境变量...${NC}"
    local env_file="/etc/profile.d/dotnet.sh"  # 系统级环境变量配置文件
    echo "export DOTNET_ROOT=$dotnet_install_dir" > "$env_file"
    echo "export PATH=\$PATH:\$DOTNET_ROOT" >> "$env_file"
    chmod 644 "$env_file"  # 确保所有用户可读取

    # 验证安装结果
    source "$env_file"  # 立即加载环境变量（当前终端生效）
    if command -v dotnet &> /dev/null; then
        local dotnet_version=$(dotnet --version 2>/dev/null)
        echo -e "${GREEN}✅ 环境变量配置成功！当前 .NET 版本：$dotnet_version${NC}"
    else
        echo -e "${YELLOW}⚠️  环境变量已配置，但当前终端未完全生效${NC}"
        echo -e "${YELLOW}   解决方案：重启终端 或 执行命令：source $env_file${NC}"
    fi
}

# 输出手动下载链接（补充官方方案）
print_manual_links() {
    echo -e "\n${YELLOW}🔗 您可以手动下载以下组件安装：${NC}"
    echo -e "${YELLOW}🔗 You can manually download and install the following components:${NC}\n"

    # 官方 SDK 链接（安装 SDK 后无需单独安装 Runtime）
    echo -e "${WHITE}• .NET SDK 10 ($arch)（推荐，包含 Runtime）:${NC}"
    echo -e "  ${CYAN}https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.100/dotnet-sdk-10.0.100-linux-$arch.tar.gz${NC}"
    
    # 官方 Runtime 链接
    echo -e "\n${WHITE}• .NET Runtime 10 ($arch)（仅运行时）:${NC}"
    echo -e "  ${CYAN}https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.0/dotnet-runtime-10.0.0-linux-$arch.tar.gz${NC}"
    
    echo -e "\n${YELLOW}📝 手动安装说明：${NC}"
    echo -e "${CYAN}1. 下载压缩包后解压到系统目录：${NC}"
    echo -e "   sudo tar -zxf dotnet-*-linux-$arch.tar.gz -C /usr/share/dotnet"
    echo -e "${CYAN}2. 配置环境变量（永久生效）：${NC}"
    echo -e "   echo 'export DOTNET_ROOT=/usr/share/dotnet' | sudo tee -a /etc/profile.d/dotnet.sh"
    echo -e "   echo 'export PATH=\$PATH:/usr/share/dotnet' | sudo tee -a /etc/profile.d/dotnet.sh"
    echo -e "   source /etc/profile.d/dotnet.sh"
}

# 主逻辑
main() {
    detect_arch
    check_admin
    install_dotnet

    # 输出最终结果
    echo -e "\n"
    if [ $error_occurred -eq 0 ]; then
        echo -e "${BOLD}${GREEN}===================================================================================================="
        echo -e "${BOLD}${GREEN}🎉 .NET Runtime 10 安装完成！${NC}"
        echo -e "${BOLD}${GREEN}🎉 .NET Runtime 10 installed successfully!${NC}"
        echo -e "${BOLD}${GREEN}===================================================================================================="${NC}
        echo -e "${YELLOW}💡 注意事项：${NC}"
        echo -e "1. 新终端会自动加载环境变量，无需手动配置"
        echo -e "2. 若当前终端无法识别 dotnet 命令，执行：source /etc/profile.d/dotnet.sh"
        echo -e "3. 建议重启系统以确保所有应用正常识别 .NET 运行时"
    else
        echo -e "${RED}===================================================================================================="
        echo -e "${BOLD}${RED}❌ 安装过程中出现错误${NC}"
        echo -e "${BOLD}${RED}❌ Errors occurred during installation${NC}"
        echo -e "\n${YELLOW}💡 解决方案：${NC}"
        echo -e "1. 检查网络连接（推荐科学上网，避免官方资源下载失败）"
        echo -e "2. 确保系统是 Debian/Ubuntu 系列（如非该系列，请使用手动安装方式）"
        echo -e "3. 清理残留后重试：sudo rm -rf $dotnet_install_dir $dotnet_script_path"
        print_manual_links
        echo -e "${RED}===================================================================================================="${NC}
    fi

    read -p "按 Enter 键退出..."
}

main